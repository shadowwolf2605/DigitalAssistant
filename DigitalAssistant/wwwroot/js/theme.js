// Светла, тъмна или като устройството тема. Изборът се пази в браузъра.
(function () {
    const root = document.documentElement;
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const icons = { light: 'bi-sun', dark: 'bi-moon-stars', auto: 'bi-circle-half' };
    const names = { light: 'светла', dark: 'тъмна', auto: 'като устройството' };

    function saved() {
        let t = null;
        try { t = localStorage.getItem('theme'); } catch { }
        return t === 'light' || t === 'dark' ? t : 'auto';
    }

    function apply(choice) {
        const dark = choice === 'dark' || (choice === 'auto' && media.matches);
        root.setAttribute('data-bs-theme', dark ? 'dark' : 'light');
        const meta = document.querySelector('meta[name=theme-color]');
        if (meta) meta.content = dark ? '#0b1220' : '#ffffff';
        document.querySelectorAll('[data-theme-value]').forEach(b => {
            const on = b.dataset.themeValue === choice;
            b.classList.toggle('active', on);
            b.setAttribute('aria-pressed', on);
        });
        document.querySelectorAll('.theme-btn').forEach(b => {
            b.querySelector('.theme-icon').className = 'bi theme-icon ' + icons[choice];
            b.setAttribute('aria-label', 'Тема: ' + names[choice]);
            b.title = 'Тема: ' + names[choice];
        });
    }

    // Плавна смяна, ако браузърът я поддържа
    function change(choice) {
        const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        if (!document.startViewTransition || reduce) { apply(choice); return; }
        root.classList.add('theme-switching');
        document.startViewTransition(() => apply(choice)).finished.finally(() => root.classList.remove('theme-switching'));
    }

    document.addEventListener('click', e => {
        const item = e.target.closest && e.target.closest('[data-theme-value]');
        if (!item) return;
        const choice = item.dataset.themeValue;
        try { localStorage.setItem('theme', choice); } catch { }
        change(choice);
    });

    // Смяна на темата на устройството и избор в друг раздел на браузъра
    media.addEventListener('change', () => { if (saved() === 'auto') apply('auto'); });
    window.addEventListener('storage', e => { if (e.key === 'theme') apply(saved()); });

    apply(saved());
})();

// При печат страницата винаги е светла – тъмните цветове на бял лист се четат трудно
(function () {
    let before = null;
    window.addEventListener('beforeprint', () => {
        before = document.documentElement.getAttribute('data-bs-theme');
        document.documentElement.setAttribute('data-bs-theme', 'light');
    });
    window.addEventListener('afterprint', () => {
        if (before) document.documentElement.setAttribute('data-bs-theme', before);
    });
})();
