// The bulk-action bar under a list with checkboxes. Without this script the bar is an ordinary row of buttons
// under the list; with it the bar shows only while rows are ticked, says how many, and stays fixed to the bottom
// of the window (see .bulk-bar in site.css). It looks at the checkboxes of its own form, so a list swapped in by
// htmx is picked up too. It also places the strip of a running bulk action on the Torrents page (placeProgress).
(function () {
    function update() {
        document.querySelectorAll('[data-bulk-bar]').forEach(function (bar) {
            const scope = bar.closest('form') || document;
            const boxes = scope.querySelectorAll('input[type="checkbox"][name="selectedIds"]');
            const ticked = Array.prototype.filter.call(boxes, function (box) { return box.checked; }).length;
            bar.hidden = ticked === 0;
            const count = bar.querySelector('[data-bulk-count]');
            if (count) {
                count.textContent = ticked === 1 ? '1 selected' : ticked + ' selected';
            }
        });
    }

    // The strip of a running bulk action (_BulkOperationsPartial) repeats the notices at the top of the page, so it
    // shows only while they are scrolled out of sight. The class goes on body, not on the strip: every poll swaps in
    // a new strip, which would otherwise show for a frame before this runs again.
    function placeProgress() {
        const notice = document.querySelector('#bulk-operations .bulk-operation');
        let inView = false;
        if (notice) {
            const header = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--header-height')) || 0;
            const box = notice.getBoundingClientRect();
            inView = box.bottom > header && box.top < window.innerHeight;
        }
        document.body.classList.toggle('bulk-notice-in-view', inView);
    }

    // A click covers the "select all" box, whose handler ticks the others without a change event of their own;
    // it runs before this one, as the target's handlers come before an ancestor's.
    document.addEventListener('click', update);
    document.addEventListener('change', update);
    document.body.addEventListener('htmx:afterSwap', function () {
        update();
        placeProgress();
    });
    // The panel's polls replace it whole (outerHTML); htmx:load fires on the new content, wherever the old one went.
    document.body.addEventListener('htmx:load', placeProgress);
    window.addEventListener('scroll', placeProgress, { passive: true });
    window.addEventListener('resize', placeProgress);
    update();
    placeProgress();
})();
