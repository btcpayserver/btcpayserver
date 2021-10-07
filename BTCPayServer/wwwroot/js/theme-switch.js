const COLOR_MODES = ["light", "dark"];
const THEME_ATTR = "data-btcpay-theme";
const STORE_ATTR = "btcpay-theme";
const systemColorMode = window.matchMedia("(prefers-color-scheme: dark)").matches ? COLOR_MODES[1] : COLOR_MODES[0];
const userColorMode = window.localStorage.getItem(STORE_ATTR);
const initialColorMode = COLOR_MODES.includes(userColorMode) ? userColorMode : systemColorMode;

function setColorMode (mode) {
    if (COLOR_MODES.includes(mode)) {
        window.localStorage.setItem(STORE_ATTR, mode);
        document.documentElement.setAttribute(THEME_ATTR, mode);
        document.getElementById("DarkThemeLinkTag").setAttribute("rel", mode === "dark" ? "stylesheet" : null);
    }
}

setColorMode(initialColorMode);
