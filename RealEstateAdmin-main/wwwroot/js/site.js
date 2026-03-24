// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Reveal animations for key UI blocks
document.addEventListener('DOMContentLoaded', () => {
    const targets = document.querySelectorAll(
        '.hero-section, .stat-card, .property-card, .search-card, .card, .table, .alert'
    );

    targets.forEach((el, index) => {
        const delay = Math.min(index * 60, 420);
        el.style.setProperty('--reveal-delay', `${delay}ms`);
        el.classList.add('reveal-item');
    });

    if (!('IntersectionObserver' in window)) {
        targets.forEach((el) => el.classList.add('reveal-in'));
        return;
    }

    const observer = new IntersectionObserver(
        (entries) => {
            entries.forEach((entry) => {
                if (entry.isIntersecting) {
                    entry.target.classList.add('reveal-in');
                    observer.unobserve(entry.target);
                }
            });
        },
        { threshold: 0.12 }
    );

    targets.forEach((el) => observer.observe(el));
});
