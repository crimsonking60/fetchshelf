// The bulk-action bar under a list with checkboxes. Without this script the bar is an ordinary row of buttons
// under the list; with it the bar shows only while rows are ticked, says how many, and stays fixed to the bottom
// of the window (see .bulk-bar in site.css). It looks at the checkboxes of its own form, so a list swapped in by
// htmx is picked up too.
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

    // A click covers the "select all" box, whose handler ticks the others without a change event of their own;
    // it runs before this one, as the target's handlers come before an ancestor's.
    document.addEventListener('click', update);
    document.addEventListener('change', update);
    document.body.addEventListener('htmx:afterSwap', update);
    update();
})();
