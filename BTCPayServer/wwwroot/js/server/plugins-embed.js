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
    const installConfirmForm = document.getElementById("plugin-install-confirm-form");
    const installConfirmPluginInput = installConfirmForm?.querySelector("input[name='plugin']");
    const installConfirmVersionInput = installConfirmForm?.querySelector("input[name='version']");
    const installConfirmName = document.getElementById("plugin-install-confirm-name");
    const installConfirmIdentifier = document.getElementById("plugin-install-confirm-identifier");
    const installConfirmVersion = document.getElementById("plugin-install-confirm-version");
    const installConfirmPending = document.getElementById("plugin-install-confirm-pending");
    const installConfirmPendingVersion = document.getElementById("plugin-install-confirm-pending-version");
    const installConfirmPendingRequestedVersion = document.getElementById("plugin-install-confirm-pending-requested-version");

    if (!directoryShell || !directoryFrame || !selectedPluginPanel || !selectedPluginOffcanvas) {
        return;
    }

    const pluginBuilderOrigin = directoryShell.dataset.origin;
    const pluginDirectoryUrl = directoryShell.dataset.iframeUrl;
    const selectedPluginPanelUrl = directoryShell.dataset.panelUrl;
    const selectedPluginPanelErrorMessage = directoryShell.dataset.panelErrorMessage || "BTCPay Server could not load the plugin panel.";
    const selectedPluginPanelRetryLabel = directoryShell.dataset.panelRetryLabel || "Retry";
    const directoryReadyTimeoutMs = 8000;
    const directoryHandshakeIntervalMs = 250;
    let hiddenPluginIdentifiers = [];
    let selectedSlug = directoryShell.dataset.selectedSlug || "";
    let directoryReady = false;
    let directoryFailed = false;
    let selectedPluginPanelRequestId = 0;
    let selectedPluginOffcanvasInstance = null;
    let directoryReadyTimeoutId = null;
    let directoryHandshakeIntervalId = null;
    let isInstallConfirmModalOpen = false;

    if (!pluginBuilderOrigin || !pluginDirectoryUrl || !selectedPluginPanelUrl) {
        if (directoryRetryButton) {
            directoryRetryButton.classList.add("d-none");
        }
        showDirectoryError();
        return;
    }

    if (hiddenPluginIdentifiersScript && hiddenPluginIdentifiersScript.textContent) {
        try {
            const parsedIdentifiers = JSON.parse(hiddenPluginIdentifiersScript.textContent);
            if (Array.isArray(parsedIdentifiers)) {
                hiddenPluginIdentifiers = parsedIdentifiers.filter(function (identifier) {
                    return typeof identifier === "string" && identifier.length > 0;
                });
            }
        } catch {
            hiddenPluginIdentifiers = [];
        }
    }

    function startDirectory() {
        directoryReady = false;
        directoryFailed = false;
        if (directoryErrorAlert) {
            directoryErrorAlert.classList.add("d-none");
        }
        directoryShell.classList.remove("d-none");

        window.clearTimeout(directoryReadyTimeoutId);
        directoryReadyTimeoutId = window.setTimeout(function () {
            if (!directoryReady) {
                showDirectoryError();
            }
        }, directoryReadyTimeoutMs);

        window.clearInterval(directoryHandshakeIntervalId);
        directoryHandshakeIntervalId = window.setInterval(function () {
            postHostContextTo(directoryFrame);
        }, directoryHandshakeIntervalMs);

        directoryFrame.src = pluginDirectoryUrl;
    }

    function showDirectoryError() {
        if (directoryFailed) {
            return;
        }

        directoryFailed = true;
        window.clearTimeout(directoryReadyTimeoutId);
        window.clearInterval(directoryHandshakeIntervalId);
        const offcanvas = getOffcanvas();
        if (offcanvas) {
            offcanvas.hide();
        }
        directoryShell.classList.add("d-none");
        if (directoryErrorAlert) {
            directoryErrorAlert.classList.remove("d-none");
        }
    }

    function markReady() {
        directoryReady = true;
        window.clearTimeout(directoryReadyTimeoutId);
        window.clearInterval(directoryHandshakeIntervalId);
    }

    function getOffcanvas() {
        if (!window.bootstrap || !window.bootstrap.Offcanvas) {
            return null;
        }

        if (!selectedPluginOffcanvasInstance) {
            selectedPluginOffcanvasInstance = window.bootstrap.Offcanvas.getOrCreateInstance(selectedPluginOffcanvas);
        }

        return selectedPluginOffcanvasInstance;
    }

    function showOffcanvas() {
        const offcanvas = getOffcanvas();
        if (offcanvas) {
            offcanvas.show();
        }
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

    function syncSelectedSlugUrl(slug) {
        const url = new URL(window.location.href);
        if (slug) {
            url.searchParams.set("selectedSlug", slug);
        } else {
            url.searchParams.delete("selectedSlug");
        }

        window.history.replaceState(window.history.state, "", url);
        directoryShell.dataset.selectedSlug = slug || "";
    }

    function renderSelectedPluginPanelError(slug) {
        selectedPluginPanel.innerHTML = '<div class="plugin-manage-panel d-flex flex-column flex-grow-1"><div class="border rounded p-3" role="alert"><div class="d-flex flex-wrap align-items-center gap-2"><span class="small text-muted me-auto"></span><button type="button" class="btn btn-secondary btn-sm flex-shrink-0"></button></div></div></div>';
        const alertMessage = selectedPluginPanel.querySelector("span");
        if (alertMessage) {
            alertMessage.textContent = selectedPluginPanelErrorMessage;
        }

        const selectedPluginPanelRetryButton = selectedPluginPanel.querySelector("button");
        if (selectedPluginPanelRetryButton) {
            selectedPluginPanelRetryButton.textContent = selectedPluginPanelRetryLabel;
            selectedPluginPanelRetryButton.addEventListener("click", function () {
                reloadSelectedPluginPanel(slug);
            });
        }
    }

    async function reloadSelectedPluginPanel(slug) {
        const requestId = ++selectedPluginPanelRequestId;

        try {
            const url = new URL(selectedPluginPanelUrl, window.location.origin);
            if (slug) {
                url.searchParams.set("slug", slug);
            }

            const response = await window.fetch(url.toString(), {
                headers: {
                    "X-Requested-With": "XMLHttpRequest"
                }
            });
            if (!response.ok) {
                throw new Error("Panel request failed");
            }

            const html = await response.text();
            if (requestId !== selectedPluginPanelRequestId) {
                return;
            }

            selectedPluginPanel.innerHTML = html;
            bindPluginDetailsFrame();
            postHostContextTo(getPluginDetailsFrame());
        } catch {
            if (requestId !== selectedPluginPanelRequestId) {
                return;
            }

            renderSelectedPluginPanelError(slug);
        }
    }

    function buildHostContext(frame) {
        const includeSelection = frame === directoryFrame;
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

        frame.contentWindow.postMessage(buildHostContext(frame), pluginBuilderOrigin);
    }

    function postHostContext() {
        if (!directoryReady || directoryFailed) {
            return;
        }

        postHostContextTo(directoryFrame);
        postHostContextTo(getPluginDetailsFrame());
    }

    function bindPluginDetailsFrame() {
        const detailsFrame = getPluginDetailsFrame();
        if (!detailsFrame || detailsFrame.dataset.hostContextBound === "true") {
            return;
        }

        detailsFrame.dataset.hostContextBound = "true";
        detailsFrame.addEventListener("load", function () {
            postHostContextTo(detailsFrame);
        });
    }

    function confirmInstallFromEmbed(data) {
        const form = selectedPluginPanel.querySelector("[data-plugin-install-form='true']");
        const Modal = window.bootstrap?.Modal;
        if (!form || isInstallConfirmModalOpen || !Modal || !installConfirmModal || !installConfirmForm || !installConfirmPluginInput || !installConfirmVersionInput || !installConfirmName || !installConfirmIdentifier || !installConfirmVersion || !installConfirmPending || !installConfirmPendingVersion || !installConfirmPendingRequestedVersion) {
            return;
        }

        const formSelectedSlug = form.dataset.selectedSlug || "";
        if (!selectedSlug || formSelectedSlug.toLowerCase() !== selectedSlug.toLowerCase()) {
            return;
        }

        const pluginIdentifier = form.querySelector("input[name='plugin']")?.value;
        if (!pluginIdentifier || pluginIdentifier.toLowerCase() !== data.identifier.toLowerCase()) {
            return;
        }

        installConfirmForm.action = form.action;
        installConfirmPluginInput.value = pluginIdentifier;
        installConfirmVersionInput.value = data.version;

        const pluginName = form.dataset.pluginName || pluginIdentifier;
        const hasPendingInstall = form.dataset.pendingInstall === "true";
        installConfirmName.textContent = pluginName;
        installConfirmIdentifier.textContent = pluginIdentifier;
        installConfirmVersion.textContent = data.version;
        installConfirmPending.classList.toggle("d-none", !hasPendingInstall);
        installConfirmPendingVersion.textContent = hasPendingInstall ? form.dataset.pendingInstallVersion || "" : "";
        installConfirmPendingRequestedVersion.textContent = hasPendingInstall ? data.version : "";

        isInstallConfirmModalOpen = true;
        Modal.getOrCreateInstance(installConfirmModal).show();
    }

    if (installConfirmForm && installConfirmPluginInput && installConfirmVersionInput) {
        installConfirmForm.addEventListener("submit", function (event) {
            if (!isInstallConfirmModalOpen || !installConfirmPluginInput.value || !installConfirmVersionInput.value) {
                event.preventDefault();
            }
        });
    }

    if (installConfirmModal && installConfirmForm) {
        installConfirmModal.addEventListener("hidden.bs.modal", function () {
            isInstallConfirmModalOpen = false;
            installConfirmForm.removeAttribute("action");

            if (installConfirmPluginInput) {
                installConfirmPluginInput.value = "";
            }

            if (installConfirmVersionInput) {
                installConfirmVersionInput.value = "";
            }

            if (installConfirmName) {
                installConfirmName.textContent = "";
            }

            if (installConfirmIdentifier) {
                installConfirmIdentifier.textContent = "";
            }

            if (installConfirmVersion) {
                installConfirmVersion.textContent = "";
            }

            if (installConfirmPending) {
                installConfirmPending.classList.add("d-none");
            }

            if (installConfirmPendingVersion) {
                installConfirmPendingVersion.textContent = "";
            }

            if (installConfirmPendingRequestedVersion) {
                installConfirmPendingRequestedVersion.textContent = "";
            }
        });
    }

    window.addEventListener("message", function (event) {
        if (directoryFailed || event.origin !== pluginBuilderOrigin) {
            return;
        }

        const sourceFrame = getFrameByWindow(event.source);
        if (!sourceFrame) {
            return;
        }

        const data = event.data;
        if (sourceFrame !== directoryFrame) {
            if (isReadyMessage(data)) {
                postHostContextTo(sourceFrame);
                return;
            }

            if (isInstallRequestMessage(data)) {
                confirmInstallFromEmbed(data);
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
            directoryFrame.style.height = Math.max(Math.ceil(data.height), 360) + "px";
            return;
        }

        if (!isSelectionMessage(data)) {
            return;
        }

        markReady();
        if (data.slug === selectedSlug) {
            showOffcanvas();
            return;
        }

        selectedSlug = data.slug;
        syncSelectedSlugUrl(selectedSlug);
        showOffcanvas();
        reloadSelectedPluginPanel(selectedSlug);
        postHostContext();
    });

    directoryFrame.addEventListener("load", function () {
        if (directoryFailed) {
            return;
        }

        postHostContextTo(directoryFrame);
    });

    if (directoryRetryButton) {
        directoryRetryButton.addEventListener("click", function () {
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

    selectedPluginOffcanvas.addEventListener("hidden.bs.offcanvas", function () {
        if (!selectedSlug) {
            return;
        }

        selectedSlug = "";
        syncSelectedSlugUrl("");
        postHostContext();
    });

    if (selectedSlug) {
        showOffcanvas();
    }

    bindPluginDetailsFrame();
    startDirectory();
})();
