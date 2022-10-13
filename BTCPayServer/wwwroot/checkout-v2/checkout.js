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

document.addEventListener('DOMContentLoaded', () => {
    // Theme Switch
    delegate('click', '.btcpay-theme-switch', e => {
        e.preventDefault()
        const current = document.documentElement.getAttribute(THEME_ATTR) || COLOR_MODES[0]
        const mode = current === COLOR_MODES[0] ? COLOR_MODES[1] : COLOR_MODES[0]
        setColorMode(mode)
        e.target.closest('.btcpay-theme-switch').blur()
    })
});

Vue.directive('collapsible', {
    bind: function (el) {
        el.transitionDuration = 350;
    },
    update: function (el, binding) {
        if (binding.oldValue !== binding.value){
            if (binding.value) {
                setTimeout(function () {
                    el.classList.remove('collapse');
                    const height = window.getComputedStyle(el).height;
                    el.classList.add('collapsing');
                    el.offsetHeight;
                    el.style.height = height;
                    setTimeout(() => {
                        el.classList.remove('collapsing');
                        el.classList.add('collapse');
                        el.style.height = null;
                        el.classList.add('show');
                    }, el.transitionDuration)
                }, 0);
            }
            else {
                el.style.height = window.getComputedStyle(el).height;
                el.classList.remove('collapse');
                el.classList.remove('show');
                el.offsetHeight;
                el.style.height = null;
                el.classList.add('collapsing');
                setTimeout(() => {
                    el.classList.add('collapse');
                    el.classList.remove("collapsing");
                }, el.transitionDuration)
            }
        }
    }
});
