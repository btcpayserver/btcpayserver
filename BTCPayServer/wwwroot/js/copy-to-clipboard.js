window.copyToClipboard = function (e, text) {
    if (navigator.clipboard) {
        e.preventDefault();
        var item = e.currentTarget;
        var data = text || item.getAttribute('data-clipboard');
        var confirm = item.querySelector('[data-clipboard-confirm]') || item;
        var message = confirm.getAttribute('data-clipboard-confirm') || 'Copied ✔';
        if (!confirm.dataset.clipboardInitialText) {
            confirm.dataset.clipboardInitialText = confirm.innerText;
            confirm.style.minWidth = confirm.getBoundingClientRect().width + 'px';
        }
        navigator.clipboard.writeText(data).then(function () {
            confirm.innerText = message;
            setTimeout(function(){ confirm.innerText = confirm.dataset.clipboardInitialText; }, 2500);
        });
        item.blur();
    }
}

window.copyUrlToClipboard = function (e) {
    window.copyToClipboard(e, window.location)
}

document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll("[data-clipboard]").forEach(item => {
        item.addEventListener("click", window.copyToClipboard)
    })
})
