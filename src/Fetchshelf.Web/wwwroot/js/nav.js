// The navigation's menus are <details>, which open and close without a script. This only makes them behave like
// menus: opening one closes the others, and a click elsewhere or Escape closes an open one.
(function () {
    const menus = () => Array.from(document.querySelectorAll('.nav-menu'));

    menus().forEach(function (menu) {
        menu.addEventListener('toggle', function () {
            if (menu.open) {
                menus().forEach(function (other) {
                    if (other !== menu) {
                        other.open = false;
                    }
                });
            }
        });
    });

    document.addEventListener('click', function (event) {
        menus().forEach(function (menu) {
            if (menu.open && !menu.contains(event.target)) {
                menu.open = false;
            }
        });
    });

    document.addEventListener('keydown', function (event) {
        if (event.key !== 'Escape') {
            return;
        }

        menus().forEach(function (menu) {
            if (menu.open) {
                menu.open = false;
                menu.querySelector('summary').focus();
            }
        });
    });
})();
