(function() {
    const COLOR_MODES = ['light', 'dark'];
    const THEME_ATTR = 'data-btcpay-theme';
    const STORE_ATTR = 'btcpay-theme';
    const mediaMatcher = window.matchMedia('(prefers-color-scheme: dark)');

    window.setColorMode = userMode => {
        if (userMode === 'system') {
            window.localStorage.removeItem(STORE_ATTR);
            document.documentElement.removeAttribute(THEME_ATTR);
        } else if (COLOR_MODES.includes(userMode)) {
            window.localStorage.setItem(STORE_ATTR, userMode);
            document.documentElement.setAttribute(THEME_ATTR, userMode);
        }
        const user = window.localStorage.getItem(STORE_ATTR);
        const system = mediaMatcher.matches ? COLOR_MODES[1] : COLOR_MODES[0];
        const mode = user || system;
        
        document.getElementById('DarkThemeLinkTag').setAttribute('rel', mode === 'dark' ? 'stylesheet' : null);
    }

    // set initial mode
    setColorMode(window.localStorage.getItem(STORE_ATTR));
    
    // listen for system mode changes
    mediaMatcher.addEventListener('change', e => {
        const userMode = window.localStorage.getItem(STORE_ATTR);
        if (!userMode) setColorMode('system');
    });
})();
