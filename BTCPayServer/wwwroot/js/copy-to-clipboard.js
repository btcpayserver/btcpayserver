function confirmCopy(el, message) {
    el.dataset.clipboardInitial = el.innerHTML;
    el.style.minWidth = el.getBoundingClientRect().width + 'px';
    const confirmHTML = `<span class="text-success">${message}</span>`;
    el.innerHTML = confirmHTML;
    if (el.dataset.clipboardHandler) {
        clearTimeout(parseInt(el.dataset.clipboardHandler));
    }
    const timeoutId = setTimeout(function () {
        if (el.innerHTML === confirmHTML) {
            el.innerHTML = el.dataset.clipboardInitial;
        }
        el.dataset.clipboardHandler = null;
    }, 2500);
    el.dataset.clipboardHandler = timeoutId.toString();
}

window.copyToClipboard = async function (e, data) {
    e.preventDefault();
    const item = e.target.closest('[data-clipboard]') || e.target.closest('[data-clipboard-target]') || e.target;
    const confirm = item.dataset.clipboardConfirmElement
        ? document.getElementById(item.dataset.clipboardConfirmElement) || item
        : item.querySelector('[data-clipboard-confirm]') || item;
    const message = confirm.getAttribute('data-clipboard-confirm') || 'Copied';
    // Check compatibility and permissions:
    // https://web.dev/async-clipboard/#security-and-permissions
    let hasPermission = true;
    if (navigator.clipboard && navigator.permissions) {
        try {
            const permissionStatus = await navigator.permissions.query({ name: 'clipboard-write', allowWithoutGesture: false });
            hasPermission = permissionStatus.state === 'granted';
        } catch (err) {}
    }
    if (navigator.clipboard && hasPermission) {
        await navigator.clipboard.writeText(data);
        confirmCopy(confirm, message);
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

document.addEventListener("DOMContentLoaded", function () {
    delegate('click', '[data-clipboard]', function (e) {
        const target = e.target.closest('[data-clipboard]');
        const data = target.getAttribute('data-clipboard') ||  target.innerText || target.value;
        window.copyToClipboard(e, data)
    })
    delegate('click', '[data-clipboard-target]', function (e) {
        const selector = e.target.closest('[data-clipboard-target]').getAttribute('data-clipboard-target');
        const target = document.querySelector(selector)
        const data = target.innerText || target.value;
        window.copyToClipboard(e, data)
    })
})
