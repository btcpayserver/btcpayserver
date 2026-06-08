(function () {
    const shell = document.getElementById("plugins-embed-shell");
    const iframe = document.getElementById("plugins-directory-iframe");
    const panel = document.getElementById("selected-plugin-panel");
    const offcanvasElement = document.getElementById("selected-plugin-offcanvas");
    const errorAlert = document.getElementById("plugins-embed-error-alert");
    const retryButton = document.getElementById("plugins-embed-retry");
    const availableSection = document.getElementById("available-plugins-section");
    const hiddenPluginIdentifiersElement = document.getElementById("plugins-embed-hidden-plugin-identifiers");
    const darkThemeLink = document.getElementById("DarkThemeLinkTag");

    if (!shell || !iframe || !panel || !offcanvasElement) {
        return;
    }

    const origin = shell.dataset.origin;
    const iframeUrl = shell.dataset.iframeUrl;
    const panelUrl = shell.dataset.panelUrl;
    const panelErrorMessage = shell.dataset.panelErrorMessage || "BTCPay Server could not load the plugin panel.";
    const panelRetryLabel = shell.dataset.panelRetryLabel || "Retry";
    let hiddenPluginIdentifiers = [];
    let selectedIdentifier = shell.dataset.selectedIdentifier || "";
    let selectedSlug = shell.dataset.selectedSlug || "";
    let ready = false;
    let directoryFailed = false;
    let pendingRequestId = 0;
    let offcanvasInstance = null;
    let readyTimeout = null;
    let handshakeInterval = null;

    if (!origin || !iframeUrl || !panelUrl) {
        if (retryButton) {
            retryButton.classList.add("d-none");
        }
        showDirectoryError();
        return;
    }

    if (hiddenPluginIdentifiersElement && hiddenPluginIdentifiersElement.textContent) {
        try {
            const parsedIdentifiers = JSON.parse(hiddenPluginIdentifiersElement.textContent);
            if (Array.isArray(parsedIdentifiers)) {
                hiddenPluginIdentifiers = parsedIdentifiers.filter(function (identifier) {
                    return typeof identifier === "string" && identifier.length > 0;
                });
            }
        } catch (error) {
            hiddenPluginIdentifiers = [];
        }
    }

    function startReadyTimeout() {
        window.clearTimeout(readyTimeout);
        readyTimeout = window.setTimeout(function () {
            if (!ready) {
                showDirectoryError();
            }
        }, 8000);
    }

    function startHandshake() {
        window.clearInterval(handshakeInterval);
        handshakeInterval = window.setInterval(function () {
            postHostContextTo(iframe);
        }, 250);
    }

    function startDirectory() {
        ready = false;
        directoryFailed = false;
        if (errorAlert) {
            errorAlert.classList.add("d-none");
        }
        shell.classList.remove("d-none");
        startReadyTimeout();
        startHandshake();
        iframe.src = iframeUrl;
    }

    function showDirectoryError() {
        if (directoryFailed) {
            return;
        }

        directoryFailed = true;
        window.clearTimeout(readyTimeout);
        window.clearInterval(handshakeInterval);
        hideOffcanvas();
        shell.classList.add("d-none");
        if (errorAlert) {
            errorAlert.classList.remove("d-none");
        }
    }

    function markReady() {
        ready = true;
        window.clearTimeout(readyTimeout);
        window.clearInterval(handshakeInterval);
    }

    function getOffcanvas() {
        if (!window.bootstrap || !window.bootstrap.Offcanvas) {
            return null;
        }

        if (!offcanvasInstance) {
            offcanvasInstance = window.bootstrap.Offcanvas.getOrCreateInstance(offcanvasElement, {
                backdrop: true,
                scroll: true
            });
        }

        return offcanvasInstance;
    }

    function showOffcanvas() {
        const offcanvas = getOffcanvas();
        if (offcanvas) {
            offcanvas.show();
        }
    }

    function hideOffcanvas() {
        const offcanvas = getOffcanvas();
        if (offcanvas) {
            offcanvas.hide();
        }
    }

    function isOffcanvasVisible() {
        return offcanvasElement.classList.contains("show");
    }

    function alignAvailableSection() {
        if (!availableSection || window.innerWidth < 992) {
            return;
        }

        const rect = availableSection.getBoundingClientRect();
        const upperBound = 96;
        const lowerBound = window.innerHeight * 0.35;
        if (rect.top >= upperBound && rect.top <= lowerBound) {
            return;
        }

        availableSection.scrollIntoView({
            behavior: "smooth",
            block: "start"
        });
    }

    function isObject(value) {
        return value !== null && typeof value === "object" && !Array.isArray(value);
    }

    function isReadyMessage(data) {
        return isObject(data) && data.type === "pb:ready";
    }

    function isSelectionMessage(data) {
        if (!isObject(data) || data.type !== "pb:plugin-selected") {
            return false;
        }

        if (typeof data.slug !== "string" || data.slug.length === 0) {
            return false;
        }

        return data.identifier === undefined || data.identifier === null || typeof data.identifier === "string";
    }

    function isHeightMessage(data) {
        return isObject(data) &&
               data.type === "pb:content-height" &&
               typeof data.height === "number" &&
               Number.isFinite(data.height) &&
               data.height > 0;
    }

    function isInstallRequestMessage(data) {
        return isObject(data) &&
               data.type === "pb:install-requested" &&
               typeof data.identifier === "string" &&
               data.identifier.length > 0 &&
               typeof data.version === "string" &&
               data.version.length > 0;
    }

    function setIframeHeight(height) {
        iframe.style.height = Math.max(Math.ceil(height), 360) + "px";
    }

    function getHostColorMode() {
        if (darkThemeLink) {
            // theme-switch.js enables dark mode by setting this link's rel to "stylesheet".
            return darkThemeLink.getAttribute("rel") === "stylesheet" ? "dark" : "light";
        }

        return document.documentElement.getAttribute("data-btcpay-theme") === "dark" ? "dark" : "light";
    }

    function getDetailsIframes() {
        return Array.prototype.slice.call(panel.querySelectorAll("iframe.plugin-manage-panel-details"));
    }

    function getFrameByWindow(sourceWindow) {
        if (iframe.contentWindow === sourceWindow) {
            return iframe;
        }

        return getDetailsIframes().find(function (detailsIframe) {
            return detailsIframe.contentWindow === sourceWindow;
        }) || null;
    }

    function buildPanelRequestUrl(identifier, slug) {
        const url = new URL(panelUrl, window.location.origin);
        if (identifier) {
            url.searchParams.set("identifier", identifier);
        }
        if (slug) {
            url.searchParams.set("slug", slug);
        }
        return url.toString();
    }

    function updateHistory(identifier, slug) {
        const url = new URL(window.location.href);
        if (identifier) {
            url.searchParams.set("selectedIdentifier", identifier);
        } else {
            url.searchParams.delete("selectedIdentifier");
        }
        if (slug) {
            url.searchParams.set("selectedSlug", slug);
        } else {
            url.searchParams.delete("selectedSlug");
        }

        window.history.replaceState(window.history.state, "", url);
        shell.dataset.selectedIdentifier = identifier || "";
        shell.dataset.selectedSlug = slug || "";
    }

    function sameSelection(identifier, slug) {
        return (identifier || "") === (selectedIdentifier || "") &&
               (slug || "") === (selectedSlug || "");
    }

    function renderPanelError(identifier, slug) {
        panel.innerHTML = '<div class="plugin-manage-panel d-flex flex-column flex-grow-1"><div class="border rounded p-3" role="alert"><div class="d-flex flex-wrap align-items-center gap-2"><span class="small text-muted me-auto"></span><button type="button" class="btn btn-secondary btn-sm flex-shrink-0"></button></div></div></div>';
        const alertMessage = panel.querySelector("span");
        if (alertMessage) {
            alertMessage.textContent = panelErrorMessage;
        }

        const retryPanelButton = panel.querySelector("button");
        if (retryPanelButton) {
            retryPanelButton.textContent = panelRetryLabel;
            retryPanelButton.addEventListener("click", function () {
                reloadPanel(identifier, slug);
            });
        }
    }

    async function reloadPanel(identifier, slug) {
        const requestId = ++pendingRequestId;
        panel.setAttribute("aria-busy", "true");

        try {
            const response = await window.fetch(buildPanelRequestUrl(identifier, slug), {
                headers: {
                    "X-Requested-With": "XMLHttpRequest"
                }
            });
            if (!response.ok) {
                throw new Error("Panel request failed");
            }

            const html = await response.text();
            if (requestId !== pendingRequestId) {
                return;
            }

            panel.innerHTML = html;
            bindDetailsIframes();
            postDetailsHostContext();
        } catch (error) {
            if (requestId !== pendingRequestId) {
                return;
            }

            renderPanelError(identifier, slug);
        } finally {
            if (requestId === pendingRequestId) {
                panel.removeAttribute("aria-busy");
            }
        }
    }

    function buildHostContext(frame) {
        const includeSelection = frame === iframe;
        return {
            type: "btcpay:host-context",
            hiddenPluginIdentifiers: hiddenPluginIdentifiers,
            selectedSlug: includeSelection ? selectedSlug : "",
            colorMode: getHostColorMode()
        };
    }

    function postHostContextTo(frame) {
        if (directoryFailed || !frame || !frame.contentWindow) {
            return;
        }

        frame.contentWindow.postMessage(buildHostContext(frame), origin);
    }

    function postDetailsHostContext() {
        getDetailsIframes().forEach(postHostContextTo);
    }

    function postHostContext() {
        if (!ready || directoryFailed) {
            return;
        }

        postHostContextTo(iframe);
        postDetailsHostContext();
    }

    function bindDetailsIframes() {
        getDetailsIframes().forEach(function (detailsIframe) {
            if (detailsIframe.dataset.hostContextBound === "true") {
                return;
            }

            detailsIframe.dataset.hostContextBound = "true";
            detailsIframe.addEventListener("load", function () {
                postHostContextTo(detailsIframe);
            });
        });
    }

    function submitInstallFromEmbed(data) {
        const form = panel.querySelector("[data-plugin-install-form='true']");
        if (!form) {
            return;
        }

        const pluginInput = form.querySelector("input[name='plugin']");
        if (!pluginInput || (pluginInput.value && pluginInput.value.toLowerCase() !== data.identifier.toLowerCase())) {
            return;
        }
        pluginInput.value = data.identifier;

        const selectedIdentifierInput = form.querySelector("input[name='selectedIdentifier']");
        if (selectedIdentifierInput) {
            selectedIdentifierInput.value = data.identifier;
        }

        let versionInput = form.querySelector("input[name='version']");
        if (!versionInput) {
            versionInput = document.createElement("input");
            versionInput.type = "hidden";
            versionInput.name = "version";
            form.appendChild(versionInput);
        }

        versionInput.value = data.version;
        form.submit();
    }

    window.addEventListener("message", function (event) {
        if (directoryFailed || event.origin !== origin) {
            return;
        }

        const sourceFrame = getFrameByWindow(event.source);
        if (!sourceFrame) {
            return;
        }

        const data = event.data;
        if (sourceFrame !== iframe) {
            if (isReadyMessage(data)) {
                postHostContextTo(sourceFrame);
                return;
            }

            if (isInstallRequestMessage(data)) {
                submitInstallFromEmbed(data);
            }
            return;
        }

        if (isReadyMessage(data)) {
            markReady();
            postHostContext();
            return;
        }

        if (isHeightMessage(data)) {
            markReady();
            setIframeHeight(data.height);
            return;
        }

        if (!isSelectionMessage(data)) {
            return;
        }

        markReady();
        if (sameSelection(data.identifier || "", data.slug)) {
            showOffcanvas();
            return;
        }

        selectedIdentifier = data.identifier || "";
        selectedSlug = data.slug;
        updateHistory(selectedIdentifier, selectedSlug);
        alignAvailableSection();
        showOffcanvas();
        reloadPanel(selectedIdentifier, selectedSlug);
        postHostContext();
    });

    iframe.addEventListener("load", function () {
        if (directoryFailed) {
            return;
        }

        postHostContextTo(iframe);
    });

    if (retryButton) {
        retryButton.addEventListener("click", function () {
            startDirectory();
        });
    }

    if (window.MutationObserver) {
        const themeObserver = new window.MutationObserver(postHostContext);
        if (darkThemeLink) {
            themeObserver.observe(darkThemeLink, { attributes: true, attributeFilter: ["rel"] });
        }
        themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ["data-btcpay-theme"] });
    }

    offcanvasElement.addEventListener("hidden.bs.offcanvas", function () {
        if (!selectedIdentifier && !selectedSlug) {
            return;
        }

        selectedIdentifier = "";
        selectedSlug = "";
        updateHistory("", "");
        postHostContext();
    });

    if (selectedIdentifier || selectedSlug) {
        showOffcanvas();
    }

    bindDetailsIframes();
    startDirectory();
})();
