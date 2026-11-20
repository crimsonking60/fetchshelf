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

// Things that stick under the header (table headings, notices) need its real height, which grows when the menu wraps.
(function () {
    const header = document.querySelector('.site-header');
    if (!header) {
        return;
    }

    function measure() {
        document.documentElement.style.setProperty('--header-height', header.getBoundingClientRect().height + 'px');
    }

    measure();
    window.addEventListener('resize', measure);
    if (window.ResizeObserver) {
        new ResizeObserver(measure).observe(header);
    }
})();
