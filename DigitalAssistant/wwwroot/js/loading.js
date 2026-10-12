// Показва, че следващата страница се зарежда: тънка лента горе и въртящ се знак в натиснатия бутон.
(function () {
    const bar = document.getElementById('pageProgress');
    let timer;

    function stop() {
        clearTimeout(timer);
        if (bar) bar.classList.remove('active');
        document.querySelectorAll('.btn.is-loading').forEach(b => b.classList.remove('is-loading'));
    }
    function start() {
        if (bar) {
            bar.classList.remove('active');
            void bar.offsetWidth; // анимацията започва отначало
            bar.classList.add('active');
        }
        clearTimeout(timer);
        // Ако страницата не се смени (напр. изтегляне на файл), всичко се връща след малко
        timer = setTimeout(stop, 8000);
    }

    // Изтеглянията и връзките към друг прозорец не сменят страницата
    const downloads = /\/(Receipt|File|Template|Export)(\/|$|\?)/i;
    function isPageChange(link, e) {
        if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return false;
        if (link.target && link.target !== '_self') return false;
        if (link.hasAttribute('download') || link.hasAttribute('data-no-loading')) return false;
        if (link.origin !== location.origin) return false;
        if (downloads.test(link.pathname)) return false;
        // Връзка към друго място на същата страница
        if (link.pathname === location.pathname && link.search === location.search && link.hash) return false;
        const href = link.getAttribute('href') || '';
        return href !== '' && !href.startsWith('#') && !href.startsWith('javascript:');
    }

    document.addEventListener('click', e => {
        const link = e.target.closest && e.target.closest('a[href]');
        if (!link) return;
        // Проверката става след другите обработчици, които може да спрат прехода
        setTimeout(() => { if (isPageChange(link, e)) start(); }, 0);
    });

    document.addEventListener('submit', e => {
        const form = e.target;
        const button = e.submitter;
        setTimeout(() => {
            if (e.defaultPrevented || form.hasAttribute('data-no-loading') || (form.target && form.target !== '_self')) return;
            const action = (button && button.getAttribute('formaction')) || form.getAttribute('action') || '';
            if (downloads.test(action)) return;
            start();
            if (button && button.classList.contains('btn')) button.classList.add('is-loading');
        }, 0);
    });

    // При връщане с бутона "Назад" страницата идва от паметта на браузъра
    window.addEventListener('pageshow', stop);
})();
