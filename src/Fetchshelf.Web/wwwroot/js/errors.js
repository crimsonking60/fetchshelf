// Safety net for htmx requests that fail before the server can render its own message (server down,
// an unexpected 5xx, an expired antiforgery token): without it the page would just silently not update.
// Search and download failures that the server does understand are rendered by the page itself.
(function () {
    function show(message) {
        let box = document.getElementById('app-error');
        if (!box) {
            box = document.createElement('div');
            box.id = 'app-error';
            box.className = 'notice error app-error';
            box.setAttribute('role', 'alert');
            box.innerHTML = '<span></span><button type="button" class="dismiss">Dismiss</button>';
            box.querySelector('button').addEventListener('click', function () { box.remove(); });
            document.querySelector('main').prepend(box);
        }
        box.firstElementChild.textContent = message;
    }

    document.body.addEventListener('htmx:responseError', function (event) {
        show('The request failed (HTTP ' + event.detail.xhr.status + '). The Fetchshelf log has the details.');
    });
    document.body.addEventListener('htmx:sendError', function () {
        show('Could not reach Fetchshelf. Check that it is still running, then try again.');
    });
    document.body.addEventListener('htmx:timeout', function () {
        show('The request timed out. Try again in a moment.');
    });
})();
