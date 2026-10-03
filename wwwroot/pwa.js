// 서비스 워커를 등록합니다. 설치 가능한 환경(HTTPS 또는 localhost)에서만 동작하며, 실패해도 앱 사용에는 영향이 없습니다.
if ('serviceWorker' in navigator) {
    window.addEventListener('load', function () {
        navigator.serviceWorker.register('/service-worker.js', { scope: '/' })
            .catch(function (error) { console.warn('서비스 워커를 등록하지 못했습니다.', error); });
    });
}
