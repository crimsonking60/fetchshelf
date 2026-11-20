// A <details data-remember-open="cookie-name"> remembers whether it was left open or closed, in that cookie, for the
// server to render it that way on the next visit (no flash). Only a click on its summary counts: the toggle event also
// fires when the page opens it itself, such as the Torrents filters while one is in use, which is no choice of the reader.
(function () {
    document.addEventListener('click', function (event) {
        const summary = event.target instanceof Element ? event.target.closest('summary') : null;
        const details = summary ? summary.parentElement : null;
        if (!details || details.tagName !== 'DETAILS' || !details.dataset.rememberOpen) {
            return;
        }
        // The click runs before the details toggles, so the new state is the opposite of the current one.
        const state = details.open ? 'closed' : 'open';
        document.cookie = details.dataset.rememberOpen + '=' + state + '; path=/; max-age=31536000; samesite=lax';
    });
})();
