// Theme menu. The server already renders <html data-theme> from the fs-theme cookie, so there is no flash on
// load; this only applies a new choice at once (no reload) and remembers it. Without JS each choice is a link
// to GET /theme, which sets the same cookie and redirects back.
(function () {
    const choices = Array.from(document.querySelectorAll('[data-theme-choice]'));

    choices.forEach(function (choice) {
        choice.addEventListener('click', function (event) {
            event.preventDefault();
            const name = choice.dataset.themeChoice;
            document.documentElement.dataset.theme = name;
            document.cookie = 'fs-theme=' + encodeURIComponent(name) + '; path=/; max-age=31536000; samesite=lax';
            choices.forEach(function (other) {
                if (other === choice) {
                    other.setAttribute('aria-current', 'true');
                } else {
                    other.removeAttribute('aria-current');
                }
            });
            const menu = choice.closest('details');
            if (menu) {
                menu.open = false;
            }
        });
    });
})();
