// Общи ефекти за всички страници: меню при превъртане, плавно появяване, броячи и бутон "нагоре".
(function () {
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    // Менюто получава сянка, когато страницата се превърти надолу
    const nav = document.getElementById('siteNav');
    const toTop = document.getElementById('toTop');
    function onScroll() {
        const y = window.scrollY;
        if (nav) nav.classList.toggle('scrolled', y > 8);
        if (toTop) toTop.classList.toggle('show', y > 600);
    }
    window.addEventListener('scroll', onScroll, { passive: true });
    onScroll();
    if (toTop) toTop.addEventListener('click', () => window.scrollTo({ top: 0, behavior: reduceMotion ? 'auto' : 'smooth' }));

    // Елементите в [data-stagger] се появяват един след друг
    document.querySelectorAll('[data-stagger]').forEach(group => {
        Array.from(group.children).forEach((child, i) => child.style.setProperty('--i', i % 12));
    });

    // Плавно появяване при превъртане
    const revealItems = document.querySelectorAll('.reveal');
    if ('IntersectionObserver' in window && !reduceMotion) {
        const observer = new IntersectionObserver(entries => {
            entries.forEach(e => {
                if (e.isIntersecting) { e.target.classList.add('is-visible'); observer.unobserve(e.target); }
            });
        }, { threshold: 0.12, rootMargin: '0px 0px -40px 0px' });
        revealItems.forEach(el => observer.observe(el));
    } else {
        revealItems.forEach(el => el.classList.add('is-visible'));
    }

    // Броячи, които "отброяват" до числото
    document.querySelectorAll('[data-count]').forEach(el => {
        const target = parseInt(el.dataset.count, 10) || 0;
        if (reduceMotion || target === 0) { el.textContent = target; return; }
        const start = performance.now(), duration = 1400;
        function frame(now) {
            const p = Math.min(1, (now - start) / duration);
            el.textContent = Math.round(target * (1 - Math.pow(1 - p, 3)));
            if (p < 1) requestAnimationFrame(frame);
        }
        requestAnimationFrame(frame);
    });

    // Раздели с теми (популярни въпроси)
    document.querySelectorAll('[data-topic]').forEach(tab => tab.addEventListener('click', () => {
        document.querySelectorAll('[data-topic]').forEach(t => { t.classList.toggle('active', t === tab); t.setAttribute('aria-selected', t === tab); });
        document.querySelectorAll('.topic-panel').forEach(p => p.classList.toggle('active', p.id === tab.dataset.topic));
    }));
})();
