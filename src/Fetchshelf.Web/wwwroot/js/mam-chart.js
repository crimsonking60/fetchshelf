// The MAM API page's chart (MamApiChartSvg): the times along its bottom in the browser's time zone, and a tooltip with
// the details of the bar or red line pointed at. The chart is replaced by htmx on every refresh, so the tooltip works
// through the document and the times are rewritten after each swap.
(function () {
    const tip = document.getElementById('mam-tip');

    function pad(number) {
        return String(number).padStart(2, '0');
    }

    function clock(iso) {
        const date = new Date(iso);
        return pad(date.getHours()) + ':' + pad(date.getMinutes());
    }

    function localizeTicks() {
        document.querySelectorAll('#mam-chart .mam-tick').forEach(function (tick) {
            tick.textContent = clock(tick.getAttribute('data-at'));
        });
    }

    function heading(target) {
        if (target.hasAttribute('data-at')) {
            const date = new Date(target.getAttribute('data-at'));
            return clock(date) + ':' + pad(date.getSeconds());
        }
        return clock(target.getAttribute('data-from')) + ' – ' + clock(target.getAttribute('data-to'));
    }

    document.addEventListener('mousemove', function (event) {
        const target = event.target instanceof Element ? event.target.closest('#mam-chart [data-tip]') : null;
        if (!target) {
            tip.hidden = true;
            return;
        }
        tip.textContent = heading(target) + '\n' + target.getAttribute('data-tip');
        tip.hidden = false;
        // Beside the pointer, kept inside the window.
        const left = Math.min(event.clientX + 14, window.innerWidth - tip.offsetWidth - 8);
        const top = Math.min(event.clientY + 14, window.innerHeight - tip.offsetHeight - 8);
        tip.style.left = Math.max(8, left) + 'px';
        tip.style.top = Math.max(8, top) + 'px';
    });

    document.body.addEventListener('htmx:afterSwap', localizeTicks);
    localizeTicks();
})();
