// Помощникът с въпроси. Всеки отговор е обикновена връзка или формуляр, така че работи и без JavaScript.
// Тук следващият въпрос се зарежда без презареждане на цялата страница, с плавна смяна.
(function () {
    const box = document.getElementById('guide');
    if (!box) return;

    async function load(url, push) {
        box.classList.add('is-busy');
        try {
            // Частта от страницата се взима от отделен адрес (partial=true), за да не я запомни браузърът
            // вместо цялата страница и бутонът „Назад“ да показва пълната страница
            const request = new URL(url, location.href);
            request.searchParams.set('partial', 'true');
            // При „Назад“/„Напред“ на браузъра търсенето не се записва отново
            if (!push && request.pathname.toLowerCase().endsWith('/assistant/search')) request.searchParams.set('again', 'true');
            const res = await fetch(request, { headers: { 'X-Guide': '1' }, credentials: 'same-origin' });
            if (!res.ok && res.status !== 429) { location.href = url; return; }
            const html = await res.text();
            box.innerHTML = html;
            const final = new URL(res.url || request.href);
            final.searchParams.delete('partial');
            const finalUrl = final.href;
            if (push && res.ok) history.pushState({ guide: true }, '', finalUrl);
            setup();
            const top = box.getBoundingClientRect().top + window.scrollY - 90;
            if (window.scrollY > top) window.scrollTo({ top, behavior: 'smooth' });
            const heading = box.querySelector('.guide-q, .guide-title');
            if (heading) heading.focus({ preventScroll: true });
            const card = box.querySelector('.guide-card');
            if (card && card.dataset.title) document.title = card.dataset.title;
        } catch {
            location.href = url;
        } finally {
            box.classList.remove('is-busy');
        }
    }

    box.addEventListener('click', e => {
        const a = e.target.closest('a[data-guide]');
        if (!a || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        e.preventDefault();
        load(a.href, true);
    });

    box.addEventListener('submit', e => {
        const form = e.target.closest('form[data-guide]');
        if (!form) return;
        e.preventDefault();
        const params = new URLSearchParams(new FormData(form));
        load(form.action + '?' + params.toString(), true);
    });

    window.addEventListener('popstate', () => load(location.href, false));

    // Копира въпроса, преди да се отвори claude.ai
    box.addEventListener('click', e => {
        const a = e.target.closest('.ask-claude');
        if (a && a.dataset.question && navigator.clipboard) navigator.clipboard.writeText(a.dataset.question).catch(() => { });
    });

    function setup() {
        const print = document.getElementById('printGuide');
        if (print) print.addEventListener('click', () => window.print());

        // „Използвай моето местоположение“: координатите се закръглят до около 100 м и се ползват само за картата
        const geo = document.getElementById('useLocation');
        const form = document.getElementById('locForm');
        if (geo && form && 'geolocation' in navigator) {
            geo.classList.remove('d-none');
            document.getElementById('geoNote').classList.remove('d-none');
            document.getElementById('locOr').classList.remove('d-none');
            geo.addEventListener('click', () => {
                geo.disabled = true;
                geo.classList.add('is-loading');
                navigator.geolocation.getCurrentPosition(pos => {
                    const lat = document.getElementById('locLat');
                    const lng = document.getElementById('locLng');
                    lat.value = pos.coords.latitude.toFixed(3);
                    lng.value = pos.coords.longitude.toFixed(3);
                    lat.disabled = lng.disabled = false;
                    const city = document.getElementById('city');
                    city.required = false;
                    city.disabled = true;
                    form.requestSubmit();
                }, () => {
                    geo.disabled = false;
                    geo.classList.remove('is-loading');
                    document.getElementById('geoNote').textContent =
                        'Не успяхме да вземем местоположението ви. Напишете населеното място по-долу.';
                }, { timeout: 15000, maximumAge: 300000 });
            });
        }
    }

    history.replaceState({ guide: true }, '', location.href);
    setup();
})();
