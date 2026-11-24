// On a phone a table marked .phone-cards.phone-labels shows each row as a card (site.css), where the column names are
// no longer above the cells. This copies each column's name onto its cells as data-label, which the card shows before
// the value ("Category: audiobooks"); a column whose heading is marked .phone-title makes its cells the card's title
// instead. Cells that already have a data-label keep it, and cells of a column without a name get none.
(function () {
    function label(table) {
        const head = table.tHead && table.tHead.rows[0];
        if (!head) {
            return;
        }
        const columns = [];
        Array.prototype.forEach.call(head.cells, function (th) {
            for (let i = 0; i < th.colSpan; i++) {
                columns.push(th);
            }
        });
        Array.prototype.forEach.call(table.tBodies, function (body) {
            Array.prototype.forEach.call(body.rows, function (row) {
                let index = 0;
                Array.prototype.forEach.call(row.cells, function (cell) {
                    const th = columns[index];
                    index += cell.colSpan;
                    if (!th || cell.hasAttribute('data-label')) {
                        return;
                    }
                    if (th.classList.contains('phone-title')) {
                        cell.classList.add('phone-title');
                        return;
                    }
                    const name = th.textContent.trim();
                    if (name) {
                        cell.setAttribute('data-label', name);
                    }
                });
            });
        });
    }

    function labelAll(root) {
        (root || document).querySelectorAll('table.phone-labels').forEach(label);
    }

    labelAll();
    // A list swapped in by htmx is new rows, before the browser paints them.
    document.body.addEventListener('htmx:afterSwap', function () {
        labelAll();
    });
})();
