// The Jobs page's list stays still between job changes. The poll (Index.cshtml) sends the version of the list on screen
// and gets nothing back until a job starts, ends or moves, so htmx leaves the list alone; meanwhile this counts the
// "Running for 2m 10s" and "in 12 min" texts on in the browser, the same way the server writes them
// (JobRunLabels.Duration and JobStatusViewModel.NextRunText), so a swapped-in list reads the same as the one it replaces.
// The times count from the server's clock (data-now), so a browser clock that is off doesn't make the texts jump.
(function () {
    const list = document.getElementById('job-list');
    if (!list) {
        return;
    }

    let offset = 0;

    function state() {
        return document.getElementById('job-list-state');
    }

    function readClock() {
        const element = state();
        const serverNow = element ? Date.parse(element.getAttribute('data-now')) : NaN;
        offset = isNaN(serverNow) ? 0 : serverNow - Date.now();
    }

    function duration(milliseconds) {
        const seconds = Math.max(0, Math.floor(milliseconds / 1000));
        if (seconds < 60) {
            return seconds + 's';
        }
        if (seconds < 3600) {
            return Math.floor(seconds / 60) + 'm ' + (seconds % 60) + 's';
        }
        return Math.floor(seconds / 3600) + 'h ' + Math.floor(seconds % 3600 / 60) + 'm';
    }

    function nextRun(milliseconds) {
        const seconds = Math.max(0, Math.ceil(milliseconds / 1000));
        if (seconds === 0) {
            return 'due now';
        }
        if (seconds < 60) {
            return 'in ' + seconds + 's';
        }
        const minutes = Math.floor((seconds + 59) / 60);
        if (minutes < 60) {
            return 'in ' + minutes + ' min';
        }
        const hours = Math.floor(minutes / 60);
        const rest = minutes % 60;
        if (minutes < 600) {
            return rest === 0 ? 'in ' + hours + ' h' : 'in ' + hours + ' h ' + rest + ' min';
        }
        const wholeHours = Math.floor((minutes + 59) / 60);
        return wholeHours < 48 ? 'in ' + wholeHours + ' h' : 'in ' + Math.floor((wholeHours + 23) / 24) + ' d';
    }

    function set(element, text) {
        if (element.textContent !== text) {
            element.textContent = text;
        }
    }

    function tick() {
        const now = Date.now() + offset;
        list.querySelectorAll('.job-elapsed[data-since]').forEach(function (element) {
            const since = Date.parse(element.getAttribute('data-since'));
            if (!isNaN(since)) {
                set(element, 'Running for ' + duration(now - since));
            }
        });
        list.querySelectorAll('.job-due[data-due]').forEach(function (element) {
            const due = Date.parse(element.getAttribute('data-due'));
            if (!isNaN(due)) {
                set(element, nextRun(due - now));
            }
        });
    }

    // The poll says which list it has; the server answers 204 No Content while that is still the list.
    document.body.addEventListener('htmx:configRequest', function (event) {
        const element = state();
        if (event.detail.elt === list && event.detail.verb === 'get' && element) {
            event.detail.parameters.version = element.getAttribute('data-version');
        }
    });

    // In the same task as the swap, before the browser paints the new list.
    document.body.addEventListener('htmx:afterSwap', function (event) {
        if (event.target === list) {
            readClock();
            tick();
        }
    });

    readClock();
    tick();
    setInterval(tick, 1000);
})();
