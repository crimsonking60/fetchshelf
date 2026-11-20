// Confirmation before a destructive action: a form or button with data-confirm="text" asks in a dialog first.
// "{n}" in the text becomes "1 selected" / "3 selected", counted from the form's ticked boxes named selectedIds, or
// the name in data-confirm-count. form.elements also holds boxes outside the form that join it by a form attribute.
// data-confirm-label names the button that goes ahead (the same verb as the button that was pressed).
(function () {
    let dialog = null;

    function build() {
        const element = document.createElement('dialog');
        element.className = 'confirm-dialog';
        element.innerHTML = '<form method="dialog"><p class="confirm-text"></p>'
            + '<div class="confirm-buttons"><button value="cancel" autofocus>Cancel</button>'
            + '<button value="ok" class="danger confirm-ok"></button></div></form>';
        document.body.appendChild(element);
        return element;
    }

    function message(source, form) {
        const name = source.dataset.confirmCount || 'selectedIds';
        const count = form
            ? Array.from(form.elements).filter(function (element) {
                return element.type === 'checkbox' && element.name === name && element.checked;
            }).length
            : 0;
        return source.dataset.confirm.replace('{n}', count + ' selected');
    }

    function ask(source, form, proceed) {
        dialog = dialog || build();
        dialog.querySelector('.confirm-text').textContent = message(source, form);
        dialog.querySelector('.confirm-ok').textContent = source.dataset.confirmLabel || 'Continue';
        dialog.returnValue = '';
        dialog.onclose = function () {
            if (dialog.returnValue === 'ok') {
                proceed();
            }
        };
        dialog.showModal();
    }

    // A button's confirmation goes first, so a form that has one on the button does not ask twice.
    document.addEventListener('click', function (event) {
        const button = event.target instanceof Element ? event.target.closest('button[data-confirm]') : null;
        if (!button || button.dataset.confirmed === 'yes' || !button.form) {
            return;
        }
        event.preventDefault();
        ask(button, button.form, function () {
            button.dataset.confirmed = 'yes';
            button.form.requestSubmit(button);
            delete button.dataset.confirmed;
        });
    }, true);

    document.addEventListener('submit', function (event) {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || !form.hasAttribute('data-confirm') || form.dataset.confirmed === 'yes') {
            return;
        }
        event.preventDefault();
        event.stopImmediatePropagation();
        ask(form, form, function () {
            form.dataset.confirmed = 'yes';
            form.requestSubmit();
            delete form.dataset.confirmed;
        });
    }, true);
})();
