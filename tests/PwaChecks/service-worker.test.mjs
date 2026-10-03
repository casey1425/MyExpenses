// wwwroot/service-worker.js의 동작을 가짜 환경(Cache, fetch)에서 검증합니다. 외부 패키지 없이 Node(18+)만 필요합니다.
import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const code = fs.readFileSync(new URL('../../wwwroot/service-worker.js', import.meta.url), 'utf8');
const ORIGIN = 'https://app.test';

function createWorker({ online = true, staleCaches = [] } = {}) {
    const listeners = {};
    const stores = new Map(staleCaches.map(name => [name, new Map()]));
    const state = { online, skipWaiting: 0, claimed: 0 };
    const fakeFetch = async request => {
        if (!state.online) throw new TypeError('Failed to fetch');
        const url = typeof request === 'string' ? request : request.url;
        return new Response('network:' + url, { status: 200 });
    };
    const caches = {
        async open(name) {
            if (!stores.has(name)) stores.set(name, new Map());
            const store = stores.get(name);
            return {
                async add(request) { store.set(new URL(request.url).pathname, await fakeFetch(request)); },
                async match(path) { const hit = store.get(path); return hit ? hit.clone() : undefined; },
                async keys() { return [...store.keys()]; },
            };
        },
        async keys() { return [...stores.keys()]; },
        async delete(name) { return stores.delete(name); },
    };
    // 서비스 워커 코드는 '/offline.html' 같은 상대 주소로 Request를 만들므로 기준 주소를 붙여 줍니다.
    class TestRequest extends Request { constructor(input, init) { super(new URL(input, ORIGIN).href, init); } }
    const self = {
        addEventListener: (type, handler) => { listeners[type] = handler; },
        skipWaiting: () => { state.skipWaiting++; return Promise.resolve(); },
        clients: { claim: () => { state.claimed++; return Promise.resolve(); } },
    };
    vm.runInNewContext(code, { self, caches, fetch: fakeFetch, Request: TestRequest, Response, URL, Promise, console });
    return { listeners, stores, state };
}

async function dispatch(worker, type, extra = {}) {
    let pending;
    const event = { waitUntil: promise => { pending = promise; }, respondWith: promise => { event.responded = true; event.response = promise; }, responded: false, ...extra };
    worker.listeners[type](event);
    if (pending) await pending;
    return event;
}
const navigate = (method = 'GET', mode = 'navigate') => ({ request: { mode, method, url: ORIGIN + '/income' } });
let cases = 0;
const test = async (name, fn) => { await fn(); cases++; };

await test('설치: 안내 화면만 미리 저장하고 즉시 활성화를 요청한다', async () => {
    const worker = createWorker();
    await dispatch(worker, 'install');
    assert.deepEqual([...worker.stores.keys()], ['myexpenses-offline-v1']);
    assert.deepEqual([...worker.stores.get('myexpenses-offline-v1').keys()], ['/offline.html']);
    assert.equal(worker.state.skipWaiting, 1);
});

await test('활성화: 이 앱의 오래된 캐시만 지우고 다른 캐시는 건드리지 않는다', async () => {
    const worker = createWorker({ staleCaches: ['myexpenses-offline-v0', 'myexpenses-offline-v1', 'other-app-cache'] });
    await dispatch(worker, 'activate');
    assert.deepEqual([...worker.stores.keys()].sort(), ['myexpenses-offline-v1', 'other-app-cache']);
    assert.equal(worker.state.claimed, 1);
});

await test('온라인 페이지 이동은 네트워크 응답을 그대로 돌려준다', async () => {
    const worker = createWorker();
    await dispatch(worker, 'install');
    const event = await dispatch(worker, 'fetch', navigate());
    assert.equal(event.responded, true);
    assert.equal(await (await event.response).text(), 'network:' + ORIGIN + '/income');
});

await test('오프라인 페이지 이동은 안내 화면(offline.html)을 보여 준다', async () => {
    const worker = createWorker();
    await dispatch(worker, 'install');
    worker.state.online = false;
    const event = await dispatch(worker, 'fetch', navigate());
    const response = await event.response;
    assert.equal(response.status, 200);
    assert.ok((await response.text()).endsWith('/offline.html'));
});

await test('안내 화면이 저장돼 있지 않으면 브라우저 기본 오류로 둔다', async () => {
    const worker = createWorker({ online: false });
    const event = await dispatch(worker, 'fetch', navigate());
    assert.equal((await event.response).type, 'error');
});

await test('페이지 이동이 아닌 요청(스크립트·API·이미지 등)은 가로채지 않는다', async () => {
    const worker = createWorker();
    await dispatch(worker, 'install');
    for (const mode of ['cors', 'no-cors', 'same-origin', 'websocket']) {
        const event = await dispatch(worker, 'fetch', navigate('GET', mode));
        assert.equal(event.responded, false, mode + ' 요청을 가로챔');
    }
});

await test('폼 전송(POST 등)은 가로채지 않는다', async () => {
    const worker = createWorker();
    for (const method of ['POST', 'PUT', 'DELETE']) {
        const event = await dispatch(worker, 'fetch', navigate(method));
        assert.equal(event.responded, false, method + ' 요청을 가로챔');
    }
});

await test('여러 번 이동해도 페이지 응답을 캐시에 저장하지 않는다', async () => {
    const worker = createWorker();
    await dispatch(worker, 'install');
    for (let i = 0; i < 3; i++) await (await dispatch(worker, 'fetch', navigate())).response;
    assert.deepEqual([...worker.stores.get('myexpenses-offline-v1').keys()], ['/offline.html']);
});

console.log(`PASS service worker behavior (${cases} cases)`);
