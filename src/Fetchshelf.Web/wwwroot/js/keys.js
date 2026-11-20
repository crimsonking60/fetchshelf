// Keyboard: "/" goes to the page's search box, "g" then a letter goes to a page, "?" opens the list in the footer.
// Nothing happens while typing in a field, with a modifier held, or with a dialog open.
(function () {
    let pendingGo = false;
    let pendingTimer = 0;

    function typing(target) {
        return target instanceof HTMLElement
            && (target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName));
    }

    function clearPending() {
        pendingGo = false;
        clearTimeout(pendingTimer);
    }

    document.addEventListener('keydown', function (event) {
        if (event.ctrlKey || event.metaKey || event.altKey || typing(event.target) || document.querySelector('dialog[open]')) {
            return;
        }

        if (pendingGo) {
            const link = document.querySelector('#shortcuts a[data-go="' + event.key + '"]');
            clearPending();
            if (link) {
                event.preventDefault();
                window.location.href = link.href;
            }
            return;
        }

        if (event.key === '/') {
            const box = document.querySelector('main input[type="text"][name="Q"]');
            if (box) {
                event.preventDefault();
                box.focus();
                box.select();
            }
        } else if (event.key === 'g') {
            pendingGo = true;
            pendingTimer = setTimeout(clearPending, 1500);
        } else if (event.key === '?') {
            const list = document.getElementById('shortcuts');
            if (list) {
                event.preventDefault();
                list.open = !list.open;
                if (list.open) {
                    list.scrollIntoView({ block: 'nearest' });
                }
            }
        }
    });
})();
