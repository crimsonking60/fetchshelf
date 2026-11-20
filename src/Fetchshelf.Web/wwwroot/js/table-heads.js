// On a phone a table scrolls sideways in a box of its own (site.css), and a box that scrolls sideways scrolls up and
// down too: a sticky heading sticks inside it, not under the page's header. So there the column names are moved down
// by script instead, to stay under the header while the rest of the table passes beneath it.
(function () {
    const phone = window.matchMedia('(max-width: 40rem)');
    let frame = 0;

    function place() {
        frame = 0;
        const header = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--header-height')) || 0;
        document.querySelectorAll('main table').forEach(function (table) {
            const head = table.tHead;
            if (!head) {
                return;
            }
            const box = table.getBoundingClientRect();
            // Never below the table's last row, so the names leave with the table.
            const shift = phone.matches ? Math.max(0, Math.min(header - box.top, box.height - head.offsetHeight)) : 0;
            head.style.transform = shift > 0 ? 'translateY(' + shift + 'px)' : '';
        });
    }

    function schedule() {
        if (!frame) {
            frame = requestAnimationFrame(place);
        }
    }

    window.addEventListener('scroll', schedule, { passive: true });
    window.addEventListener('resize', schedule);
    phone.addEventListener('change', schedule);
    // A list swapped in by htmx is a new table, and a changed header height (a wrapped menu) moves the line.
    document.addEventListener('htmx:afterSettle', schedule);
    if (window.ResizeObserver) {
        const siteHeader = document.querySelector('.site-header');
        if (siteHeader) {
            new ResizeObserver(schedule).observe(siteHeader);
        }
    }
    schedule();
})();
