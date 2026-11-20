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

    const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

    function startOfDay(date) {
        return new Date(date.getFullYear(), date.getMonth(), date.getDate());
    }

    // The short form for tables ("3 h ago", "yesterday 21:14", "19 Nov 15:43"). The exact time stays in the title.
    function short(date, now) {
        const clock = pad(date.getHours()) + ':' + pad(date.getMinutes());
        const minutes = Math.floor((now - date) / 60000);
        if (minutes < 0) {
            return format(date);
        }
        if (minutes < 1) {
            return 'just now';
        }
        if (minutes < 60) {
            return minutes + ' min ago';
        }
        if (minutes < 360) {
            return Math.floor(minutes / 60) + ' h ago';
        }
        const days = Math.round((startOfDay(now) - startOfDay(date)) / 86400000);
        if (days === 0) {
            return 'today ' + clock;
        }
        if (days === 1) {
            return 'yesterday ' + clock;
        }
        if (date.getFullYear() === now.getFullYear()) {
            return date.getDate() + ' ' + months[date.getMonth()] + ' ' + clock;
        }
        return date.getFullYear() + '-' + pad(date.getMonth() + 1) + '-' + pad(date.getDate());
    }

    function localize() {
        const now = new Date();
        document.querySelectorAll('time.local-time').forEach(function (element) {
            const date = new Date(element.getAttribute('datetime'));
            if (isNaN(date)) {
                return;
            }
            if (element.hasAttribute('data-short')) {
                element.textContent = short(date, now);
                element.title = format(date);
            } else {
                element.textContent = format(date);
            }
        });
    }

    document.body.addEventListener('htmx:afterSwap', localize);
    localize();
    // "3 h ago" goes stale on a page left open.
    setInterval(localize, 60000);
})();
