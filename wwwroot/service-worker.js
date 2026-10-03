// MyExpenses 서비스 워커
// 앱을 홈 화면에 설치할 수 있게 하고, 인터넷이 끊긴 상태에서 페이지를 열면 안내 화면(offline.html)을 보여 줍니다.
// 이 앱은 서버와 실시간으로 연결되어 동작하고 로그인 정보·가계부 데이터를 다루므로, 페이지·API 응답은 캐시하지 않습니다.
const CACHE_PREFIX = 'myexpenses-offline-';
const CACHE_NAME = CACHE_PREFIX + 'v1';
const OFFLINE_URL = '/offline.html';

self.addEventListener('install', event => {
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then(cache => cache.add(new Request(OFFLINE_URL, { cache: 'reload' })))
            .then(() => self.skipWaiting()));
});

self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys()
            .then(keys => Promise.all(keys
                .filter(key => key.startsWith(CACHE_PREFIX) && key !== CACHE_NAME)
                .map(key => caches.delete(key))))
            .then(() => self.clients.claim()));
});

self.addEventListener('fetch', event => {
    const request = event.request;
    // 페이지 이동(GET)만 다룹니다. 폼 전송, 정적 파일, Blazor 연결 등은 브라우저가 그대로 처리합니다.
    if (request.mode !== 'navigate' || request.method !== 'GET')
        return;

    event.respondWith((async () => {
        try {
            return await fetch(request);
        } catch {
            const cache = await caches.open(CACHE_NAME);
            return (await cache.match(OFFLINE_URL)) || Response.error();
        }
    })());
});
