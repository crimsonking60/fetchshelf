// Theme switcher. The server already renders <html data-theme> from the fs-theme cookie, so there is
// no flash on load; this only applies a new choice immediately (no reload) and remembers it.
// Without JS the form falls back to GET /theme, which sets the same cookie and redirects back.
(function () {
    const select = document.querySelector('[data-theme-select]');
    if (!select) {
        return;
    }

    select.addEventListener('change', function () {
        document.documentElement.dataset.theme = select.value;
        document.cookie = 'fs-theme=' + encodeURIComponent(select.value) + '; path=/; max-age=31536000; samesite=lax';
    });
})();
