// 화면 모드(시스템/밝게/어둡게)와 글자 크기를 적용합니다. 깜빡임이 없도록 <head>에서 먼저 실행합니다.
(function () {
    var root = document.documentElement;
    var THEME_KEY = 'myexpenses.theme', FONT_KEY = 'myexpenses.font';
    var query = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;

    function read(key) { try { return localStorage.getItem(key); } catch (e) { return null; } }
    function write(key, value) {
        try { if (value) localStorage.setItem(key, value); else localStorage.removeItem(key); } catch (e) { }
    }
    function preference() { var v = read(THEME_KEY); return v === 'light' || v === 'dark' ? v : 'system'; }
    function effective() { var p = preference(); return p === 'system' ? (query && query.matches ? 'dark' : 'light') : p; }

    function sync() {
        var theme = effective(), pref = preference(), large = read(FONT_KEY) === 'large';
        root.setAttribute('data-theme', theme);
        root.setAttribute('data-bs-theme', theme);
        root.setAttribute('data-theme-pref', pref);
        if (large) root.setAttribute('data-font', 'large'); else root.removeAttribute('data-font');
        var buttons = document.querySelectorAll('[data-theme-choice],[data-font-choice],[data-theme-toggle]');
        for (var i = 0; i < buttons.length; i++) {
            var b = buttons[i];
            if (b.hasAttribute('data-theme-choice')) b.setAttribute('aria-pressed', String(b.getAttribute('data-theme-choice') === pref));
            else if (b.hasAttribute('data-font-choice')) b.setAttribute('aria-pressed', String((b.getAttribute('data-font-choice') === 'large') === large));
            else b.setAttribute('aria-label', theme === 'dark' ? '밝은 화면으로 전환' : '어두운 화면으로 전환');
        }
    }

    sync();
    if (query && query.addEventListener) query.addEventListener('change', sync);

    document.addEventListener('click', function (event) {
        var target = event.target.closest ? event.target.closest('[data-theme-choice],[data-font-choice],[data-theme-toggle]') : null;
        if (!target) return;
        if (target.hasAttribute('data-theme-choice')) {
            var choice = target.getAttribute('data-theme-choice');
            write(THEME_KEY, choice === 'system' ? null : choice);
        } else if (target.hasAttribute('data-font-choice')) {
            write(FONT_KEY, target.getAttribute('data-font-choice') === 'large' ? 'large' : null);
        } else {
            write(THEME_KEY, effective() === 'dark' ? 'light' : 'dark');
        }
        sync();
    });

    // 화면이 새로 그려진 뒤(페이지 이동 등)에도 버튼 상태를 맞춥니다.
    document.addEventListener('DOMContentLoaded', function () {
        sync();
        if (window.MutationObserver) {
            var pending = false;
            new MutationObserver(function () {
                if (pending) return;
                pending = true;
                setTimeout(function () { pending = false; sync(); }, 50);
            }).observe(document.body, { childList: true, subtree: true });
        }
    });
})();
