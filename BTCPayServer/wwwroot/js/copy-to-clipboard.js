const confirmCopy = (el, message) => {
    el.innerText = message;
    setTimeout(function () {
        el.innerHTML = el.dataset.clipboardInitial;
    }, 2500);
}

window.copyToClipboard = function (e, data) {
    e.preventDefault();
    const item = e.target.closest('[data-clipboard]') || e.target.closest('[data-clipboard-target]') || e.target;
    const confirm = item.querySelector('[data-clipboard-confirm]') || item;
    const message = confirm.getAttribute('data-clipboard-confirm') || 'Copied ✔';
    if (!confirm.dataset.clipboardInitial) {
        confirm.dataset.clipboardInitial = confirm.innerHTML;
        confirm.style.minWidth = confirm.getBoundingClientRect().width + 'px';
    }
    if (navigator.clipboard) {
        navigator.clipboard.writeText(data).then(function () {
            confirmCopy(confirm, message);
        });
    } else {
        const copyEl = document.createElement('textarea');
        copyEl.style.position = 'absolute';
        copyEl.style.opacity = '0';
        copyEl.value = data;
        document.body.appendChild(copyEl);
        copyEl.select();
        document.execCommand('copy');
        copyEl.remove();
        confirmCopy(confirm, message);
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
