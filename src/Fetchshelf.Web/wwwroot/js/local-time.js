// Shows every <time class="local-time"> in the browser's local time as YYYY-MM-DD HH:MM:SS.
// The server renders the instant in UTC (see LocalTimeTagHelper); this rewrites the text, on load and
// whenever htmx brings in new content (job list polling, row swaps, refreshed lists).
//
// New content must be rewritten on htmx:afterSwap, which fires in the same task as the swap, so the browser
// never paints the longer server text. htmx:load is queued behind htmx's settle delay (20 ms by default):
// listening to it let every poll of the Jobs page paint the wider "... UTC" text for a frame, which made
// the table's columns shift on each refresh.
(function () {
    function pad(number) {
        return String(number).padStart(2, '0');
    }

    function format(date) {
        return date.getFullYear() + '-' + pad(date.getMonth() + 1) + '-' + pad(date.getDate())
            + ' ' + pad(date.getHours()) + ':' + pad(date.getMinutes()) + ':' + pad(date.getSeconds());
    }

    function localize() {
        document.querySelectorAll('time.local-time').forEach(function (element) {
            const date = new Date(element.getAttribute('datetime'));
            if (!isNaN(date)) {
                element.textContent = format(date);
            }
        });
    }

    document.body.addEventListener('htmx:afterSwap', localize);
    localize();
})();
