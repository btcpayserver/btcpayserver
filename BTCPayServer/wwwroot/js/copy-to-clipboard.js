window.copyToClipboard = function (e, data) {
    if (navigator.clipboard) {
        e.preventDefault();
        const item = e.target.closest('[data-clipboard]');
        const confirm = item.querySelector('[data-clipboard-confirm]') || item;
        const message = confirm.getAttribute('data-clipboard-confirm') || 'Copied ✔';
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
    delegate('click', '[data-clipboard]', e => {
        const data = e.target.closest('[data-clipboard]').getAttribute('data-clipboard')
        window.copyToClipboard(e, data)
    })
    delegate('click', '[data-clipboard-target]', e => {
        const selector = e.target.closest('[data-clipboard-target]').getAttribute('data-clipboard-target')
        const target = document.querySelector(selector)
        const data = target.innerText
        window.copyToClipboard(e, data)
    })
})
