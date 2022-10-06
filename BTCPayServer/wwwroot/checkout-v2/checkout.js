const urlParams = {};
(window.onpopstate = function () {
    let match,
        pl = /\+/g,  // Regex for replacing addition symbol with a space
        search = /([^&=]+)=?([^&]*)/g,
        decode = function (s) { return decodeURIComponent(s.replace(pl, " ")); },
        query = window.location.search.substring(1);

    while (match = search.exec(query)) {
        urlParams[decode(match[1])] = decode(match[2]);
    }
})();

document.addEventListener("DOMContentLoaded", () => {
    // Theme Switch
    delegate('click', '.btcpay-theme-switch', e => {
        e.preventDefault()
        const current = document.documentElement.getAttribute(THEME_ATTR) || COLOR_MODES[0]
        const mode = current === COLOR_MODES[0] ? COLOR_MODES[1] : COLOR_MODES[0]
        setColorMode(mode)
        e.target.closest('.btcpay-theme-switch').blur()
    })
});
