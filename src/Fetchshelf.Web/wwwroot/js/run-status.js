// The "running" pill in the menu bar: asks the server which background jobs run, now and every few seconds while
// the page is visible, so "Run now" and the scheduled jobs show on every page, not only on Jobs.
(function () {
    const pill = document.getElementById('run-status');
    if (!pill) {
        return;
    }

    function show(names) {
        if (names.length === 0) {
            pill.hidden = true;
            return;
        }
        pill.textContent = names.length === 1 ? names[0] + ' running' : names.length + ' jobs running';
        pill.title = names.join(', ') + ' (open the Jobs page)';
        pill.hidden = false;
    }

    function poll() {
        if (document.hidden) {
            return;
        }
        fetch('/jobs?handler=Running', { headers: { Accept: 'application/json' } })
            .then(function (response) { return response.ok ? response.json() : []; })
            .then(show)
            .catch(function () { pill.hidden = true; });
    }

    poll();
    setInterval(poll, 5000);
    document.addEventListener('visibilitychange', poll);
    // A job started from the Jobs page shows at once, not after the next poll.
    document.body.addEventListener('htmx:afterSwap', function (event) {
        if (event.target && event.target.id === 'job-list') {
            poll();
        }
    });

    // "Run now" answers at once: the button reads "Starting..." and cannot be pressed twice until the list comes back.
    document.body.addEventListener('htmx:beforeRequest', function (event) {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || !form.closest('#job-list')) {
            return;
        }
        const button = form.querySelector('button[type="submit"]');
        if (button && !button.disabled) {
            button.disabled = true;
            button.textContent = button.textContent.trim() === 'Cancel' ? 'Cancelling...' : 'Starting...';
        }
    });
})();
