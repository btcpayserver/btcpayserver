const confirmCopy = (el, message) => {
    if (!el.dataset.clipboardInitial) {
        el.dataset.clipboardInitial = el.innerHTML;
        el.style.minWidth = el.getBoundingClientRect().width + 'px';
    }
    el.innerHTML = `<span class="text-success">${message}</span>`;
    setTimeout(function () {
        el.innerHTML = el.dataset.clipboardInitial;
    }, 2500);
}

window.copyToClipboard = function (e, data) {
    e.preventDefault();
    const item = e.target.closest('[data-clipboard]') || e.target.closest('[data-clipboard-target]') || e.target;
    const confirm = item.dataset.clipboardConfirmElement
        ? document.getElementById(item.dataset.clipboardConfirmElement) || item
        : item.querySelector('[data-clipboard-confirm]') || item;
    const message = confirm.getAttribute('data-clipboard-confirm') || 'Copied';
    if (navigator.clipboard) {
        navigator.clipboard.writeText(data).then(() => {
            confirmCopy(confirm, message);
        });
    }
    item.blur();
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
