(function () {
    const directoryShell = document.getElementById("plugins-embed-shell");
    const directoryFrame = document.getElementById("plugins-directory-iframe");
    const selectedPluginPanel = document.getElementById("selected-plugin-panel");
    const selectedPluginOffcanvas = document.getElementById("selected-plugin-offcanvas");
    const directoryErrorAlert = document.getElementById("plugins-embed-error-alert");
    const directoryRetryButton = document.getElementById("plugins-embed-retry");
    const hiddenPluginIdentifiersScript = document.getElementById("plugins-embed-hidden-plugin-identifiers");
    const darkThemeLink = document.getElementById("DarkThemeLinkTag");
    const installConfirmModal = document.getElementById("plugin-install-confirm-modal");
    const installConfirmPluginInput = installConfirmModal.querySelector("input[name='plugin']");
    const installConfirmVersionInput = installConfirmModal.querySelector("input[name='version']");
    const installConfirmName = document.getElementById("plugin-install-confirm-name");
    const installConfirmIdentifier = document.getElementById("plugin-install-confirm-identifier");
    const installConfirmVersion = document.getElementById("plugin-install-confirm-version");
    const installConfirmPending = document.getElementById("plugin-install-confirm-pending");
    const installConfirmPendingVersion = document.getElementById("plugin-install-confirm-pending-version");
    const installConfirmPendingRequestedVersion = document.getElementById("plugin-install-confirm-pending-requested-version");

    const pluginDirectoryUrl = directoryShell.dataset.iframeUrl;
    const selectedPluginPanelUrl = directoryShell.dataset.panelUrl;
    const selectedPluginPanelErrorMessage = directoryShell.dataset.panelErrorMessage;
    const selectedPluginPanelLoadingMessage = directoryShell.dataset.panelLoadingMessage;
    const hiddenPluginIdentifiers = JSON.parse(hiddenPluginIdentifiersScript.textContent);
    const directoryReadyTimeoutMs = 8000;
    let selectedSlug = directoryShell.dataset.selectedSlug || "";
    let directoryReady = false;
    let directoryFailed = false;
    let selectedPluginPanelRequestId = 0;
    let directoryReadyTimeoutId = null;
    let isInstallConfirmModalOpen = false;

    if (!pluginDirectoryUrl || !selectedPluginPanelUrl) {
        directoryRetryButton.classList.add("d-none");
        showDirectoryError();
        return;
    }
    const pluginBuilderOrigin = new URL(pluginDirectoryUrl).origin;

    function startDirectory() {
        directoryReady = false;
        directoryFailed = false;
        directoryErrorAlert.classList.add("d-none");
        directoryShell.classList.remove("d-none");

        window.clearTimeout(directoryReadyTimeoutId);
        directoryReadyTimeoutId = window.setTimeout(function () {
            if (!directoryReady) {
                showDirectoryError();
            }
        }, directoryReadyTimeoutMs);

        directoryFrame.src = pluginDirectoryUrl;
    }

    function showDirectoryError() {
        if (directoryFailed) {
            return;
        }

        directoryFailed = true;
        window.clearTimeout(directoryReadyTimeoutId);
        getOffcanvas().hide();
        directoryShell.classList.add("d-none");
        directoryErrorAlert.classList.remove("d-none");
    }

    function markReady() {
        directoryReady = true;
        window.clearTimeout(directoryReadyTimeoutId);
    }

    function getOffcanvas() {
        return window.bootstrap.Offcanvas.getOrCreateInstance(selectedPluginOffcanvas);
    }

    function isObject(value) {
        return value !== null && typeof value === "object" && !Array.isArray(value);
    }

    function isReadyMessage(data) {
        return isObject(data) && data.type === "pb:ready";
    }

    function isSelectionMessage(data) {
        return isObject(data) &&
               data.type === "pb:plugin-selected" &&
               typeof data.slug === "string" &&
               data.slug.length > 0;
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

    function getHostColorMode() {
        if (darkThemeLink) {
            // theme-switch.js enables dark mode by setting this link's rel to "stylesheet".
            return darkThemeLink.getAttribute("rel") === "stylesheet" ? "dark" : "light";
        }

        return document.documentElement.getAttribute("data-btcpay-theme") === "dark" ? "dark" : "light";
    }

    function getPluginDetailsFrame() {
        return selectedPluginPanel.querySelector("iframe.plugin-manage-panel-details");
    }

    function getFrameByWindow(sourceWindow) {
        if (directoryFrame.contentWindow === sourceWindow) {
            return directoryFrame;
        }

        const detailsFrame = getPluginDetailsFrame();
        return detailsFrame?.contentWindow === sourceWindow ? detailsFrame : null;
    }

    function usesOpaqueOrigin(frame) {
        return !frame.sandbox.contains("allow-same-origin");
    }

    function syncSelectedSlugUrl(slug) {
        const url = new URL(window.location.href);
        if (slug) {
            url.searchParams.set("selectedSlug", slug);
        } else {
            url.searchParams.delete("selectedSlug");
        }

        window.history.replaceState(window.history.state, "", url);
    }

    function renderSelectedPluginPanelLoading() {
        selectedPluginPanel.setAttribute("aria-busy", "true");
        selectedPluginPanel.innerHTML = '<div class="d-flex align-items-center gap-2 py-3 text-muted" role="status"><span class="spinner-border spinner-border-sm" aria-hidden="true"></span><span data-plugin-panel-loading-message></span></div>';
        selectedPluginPanel.querySelector("[data-plugin-panel-loading-message]").textContent = selectedPluginPanelLoadingMessage;
    }

    function renderSelectedPluginPanelError(slug) {
        selectedPluginPanel.innerHTML = '<div class="border rounded p-3" role="alert"><div class="d-flex flex-wrap align-items-center gap-2"><span class="small text-muted me-auto"></span><button type="button" class="btn btn-secondary btn-sm flex-shrink-0"></button></div></div>';
        const alertMessage = selectedPluginPanel.querySelector("span");
        alertMessage.textContent = selectedPluginPanelErrorMessage;

        const selectedPluginPanelRetryButton = selectedPluginPanel.querySelector("button");
        selectedPluginPanelRetryButton.textContent = directoryRetryButton.textContent;
        selectedPluginPanelRetryButton.addEventListener("click", function () {
            reloadSelectedPluginPanel(slug);
        });
    }

    async function reloadSelectedPluginPanel(slug) {
        const requestId = ++selectedPluginPanelRequestId;
        renderSelectedPluginPanelLoading();

        try {
            const url = new URL(selectedPluginPanelUrl, window.location.origin);
            url.searchParams.set("slug", slug);

            const response = await window.fetch(url.toString());
            if (!response.ok) {
                throw new Error("Panel request failed");
            }

            const html = await response.text();
            if (requestId !== selectedPluginPanelRequestId) {
                return;
            }

            selectedPluginPanel.innerHTML = html;
            bindPluginDetailsFrame();
        } catch {
            if (requestId !== selectedPluginPanelRequestId) {
                return;
            }

            renderSelectedPluginPanelError(slug);
        } finally {
            if (requestId === selectedPluginPanelRequestId) {
                selectedPluginPanel.removeAttribute("aria-busy");
            }
        }
    }

    function postHostContextTo(frame) {
        if (directoryFailed || !frame || !frame.contentWindow) {
            return;
        }

        frame.contentWindow.postMessage({
            type: "btcpay:host-context",
            hiddenPluginIdentifiers: hiddenPluginIdentifiers,
            colorMode: getHostColorMode()
        }, usesOpaqueOrigin(frame) ? "*" : pluginBuilderOrigin);
    }

    function postHostContext() {
        if (!directoryReady) {
            return;
        }

        postHostContextTo(directoryFrame);
        postHostContextTo(getPluginDetailsFrame());
    }

    function bindPluginDetailsFrame() {
        const detailsFrame = getPluginDetailsFrame();
        if (!detailsFrame) {
            return;
        }

        detailsFrame.addEventListener("load", function () {
            postHostContextTo(detailsFrame);
        });
    }

    function confirmInstallFromEmbed(data, sourceFrame) {
        if (!selectedSlug || isInstallConfirmModalOpen) {
            return;
        }

        const pluginIdentifier = sourceFrame.dataset.pluginIdentifier;
        if (!pluginIdentifier || pluginIdentifier.toLowerCase() !== data.identifier.toLowerCase()) {
            return;
        }

        installConfirmPluginInput.value = pluginIdentifier;
        installConfirmVersionInput.value = data.version;

        const pluginName = sourceFrame.dataset.pluginName || pluginIdentifier;
        const hasPendingInstall = sourceFrame.dataset.pendingInstall === "true";
        installConfirmName.textContent = pluginName;
        installConfirmIdentifier.textContent = pluginIdentifier;
        installConfirmVersion.textContent = data.version;
        installConfirmPending.classList.toggle("d-none", !hasPendingInstall);
        installConfirmPendingVersion.textContent = hasPendingInstall ? sourceFrame.dataset.pendingInstallVersion || "" : "";
        installConfirmPendingRequestedVersion.textContent = hasPendingInstall ? data.version : "";

        isInstallConfirmModalOpen = true;
        window.bootstrap.Modal.getOrCreateInstance(installConfirmModal).show();
    }

    installConfirmModal.addEventListener("hidden.bs.modal", function () {
        isInstallConfirmModalOpen = false;
    });

    window.addEventListener("message", function (event) {
        if (directoryFailed) {
            return;
        }

        const sourceFrame = getFrameByWindow(event.source);
        if (!sourceFrame) {
            return;
        }

        // Frames without allow-same-origin have an opaque origin serialized as "null".
        const expectedOrigin = usesOpaqueOrigin(sourceFrame) ? "null" : pluginBuilderOrigin;
        if (event.origin !== expectedOrigin) {
            return;
        }

        const data = event.data;
        if (sourceFrame !== directoryFrame) {
            if (isReadyMessage(data)) {
                postHostContextTo(sourceFrame);
                return;
            }

            if (isInstallRequestMessage(data)) {
                confirmInstallFromEmbed(data, sourceFrame);
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
            directoryFrame.style.height = Math.ceil(data.height) + "px";
            return;
        }

        if (!isSelectionMessage(data)) {
            return;
        }

        markReady();
        if (data.slug === selectedSlug) {
            getOffcanvas().show();
            return;
        }

        selectedSlug = data.slug;
        syncSelectedSlugUrl(selectedSlug);
        getOffcanvas().show();
        reloadSelectedPluginPanel(selectedSlug);
    });

    directoryFrame.addEventListener("load", function () {
        postHostContextTo(directoryFrame);
    });

    directoryRetryButton.addEventListener("click", startDirectory);

    const themeObserver = new window.MutationObserver(postHostContext);
    if (darkThemeLink) {
        themeObserver.observe(darkThemeLink, { attributes: true, attributeFilter: ["rel"] });
    } else {
        themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ["data-btcpay-theme"] });
    }

    selectedPluginOffcanvas.addEventListener("hidden.bs.offcanvas", function () {
        if (!selectedSlug) {
            return;
        }

        selectedSlug = "";
        syncSelectedSlugUrl("");
    });

    if (selectedSlug) {
        getOffcanvas().show();
    }

    bindPluginDetailsFrame();
    startDirectory();
})();
