/// <reference path="vaultbridge.js" />
/// file: vaultbridge.js

var vaultui = (function () {

    /**
    * @param {string} type
    * @param {string} txt
    * @param {string} id
    */
    function VaultFeedback(type, txt, id) {
        var self = this;
        this.type = type;
        this.txt = txt;
        this.id = id;
        /**
        * @param {string} str
        * @param {string} by
        */
        this.replace = function (str, by) {
            return new VaultFeedback(self.type, self.txt.replace(str, by), self.id);
        };
    }

    var VaultFeedbacks = {
        vaultLoading: new VaultFeedback("?", "Checking BTCPay Server Vault is running...", "vault-loading"),
        vaultDenied: new VaultFeedback("failed", "The user declined access to the vault.", "vault-denied"),
        vaultGranted: new VaultFeedback("ok", "Access to vault granted by owner.", "vault-granted"),
        noVault: new VaultFeedback("failed", "BTCPay Server Vault does not seem to be running, you can download it on <a target=\"_blank\" href=\"https://github.com/btcpayserver/BTCPayServer.Vault/releases/latest\">Github</a>.", "no-vault"),
        noWebsockets: new VaultFeedback("failed", "Web sockets are not supported by the browser.", "no-websocket"),
        errorWebsockets: new VaultFeedback("failed", "Error of the websocket while connecting to the backend.", "error-websocket"),
        bridgeConnected: new VaultFeedback("ok", "BTCPayServer successfully connected to the vault.", "bridge-connected"),
        vaultNeedUpdate: new VaultFeedback("failed", "Your BTCPay Server Vault version is outdated. Please <a target=\"_blank\" href=\"https://github.com/btcpayserver/BTCPayServer.Vault/releases/latest\">download</a> the latest version.", "vault-outdated"),
        noDevice: new VaultFeedback("failed", "No device connected.", "no-device"),
        needInitialized: new VaultFeedback("failed", "The device has not been initialized.", "need-initialized"),
        fetchingDevice: new VaultFeedback("?", "Fetching device...", "fetching-device"),
        deviceFound: new VaultFeedback("ok", "Device found: {{0}}", "device-selected"),
        fetchingXpubs: new VaultFeedback("?", "Fetching public keys...", "fetching-xpubs"),
        askXpubs: new VaultFeedback("?", "Select your address type and account", "fetching-xpubs"),
        fetchedXpubs: new VaultFeedback("ok", "Public keys successfully fetched.", "xpubs-fetched"),
        unexpectedError: new VaultFeedback("failed", "An unexpected error happened. ({{0}})", "unknown-error"),
        invalidNetwork: new VaultFeedback("failed", "The device is targeting a different chain.", "invalid-network"),
        needPin: new VaultFeedback("?", "Enter the pin.", "need-pin"),
        incorrectPin: new VaultFeedback("failed", "Incorrect pin code.", "incorrect-pin"),
        invalidPasswordConfirmation: new VaultFeedback("failed", "Invalid password confirmation.", "invalid-password-confirm"),
        wrongWallet: new VaultFeedback("failed", "This device can't sign the transaction. (Wrong device, wrong passphrase or wrong device fingerprint in your wallet settings)", "wrong-wallet"),
        wrongKeyPath: new VaultFeedback("failed", "This device can't sign the transaction. (The wallet keypath in your wallet settings seems incorrect)", "wrong-keypath"),
        needPassphrase: new VaultFeedback("?", "Enter the passphrase.", "need-passphrase"),
        needPassphraseOnDevice: new VaultFeedback("?", "Please, enter the passphrase on the device.", "need-passphrase-on-device"),
        signingTransaction: new VaultFeedback("?", "Please review and confirm the transaction on your device...", "ask-signing"),
        reviewAddress: new VaultFeedback("?", "Sending... Please review the address on your device...", "ask-signing"),
        signingRejected: new VaultFeedback("failed", "The user refused to sign the transaction", "user-reject"),
    };

    /**
     * @param {string} backend_uri
     */
    function VaultBridgeUI(backend_uri) {
        /**
        * @type {VaultBridgeUI}
        */
        var self = this;
        this.backend_uri = backend_uri;
        /**
        * @type {vault.VaultBridge}
        */
        this.bridge = null;

        /**
        * @type {string}
        */
        this.psbt = null;

        this.xpub = null;

        this.retryShowing = false;

        function showRetry() {
            var button = $("#vault-retry");
            self.retryShowing = true;
            button.show();
        }

        this.currentFeedback = 1;

        /**
        * @param {VaultFeedback} feedback
        */
        function show(feedback) {
            var icon = $(".vault-feedback.vault-feedback" + self.currentFeedback + " " + ".vault-feedback-icon");
            icon.removeClass();
            icon.addClass("vault-feedback-icon mt-1 me-2");
            if (feedback.type == "?") {
                icon.addClass("fa fa-question-circle feedback-icon-loading");
            }
            else if (feedback.type == "ok") {
                icon.addClass("fa fa-check-circle feedback-icon-success");
            }
            else if (feedback.type == "failed") {
                icon.addClass("fa fa-times-circle feedback-icon-failed");
                showRetry();
            }
            var content = $(".vault-feedback.vault-feedback" + self.currentFeedback + " " + ".vault-feedback-content");
            content.html(feedback.txt);
            if (feedback.type === 'ok')
                self.currentFeedback++;
            if (feedback.type === 'failed')
                self.currentFeedback = 1;
        }
        function showError(json) {
            if (json.hasOwnProperty("error")) {
                for (var key in VaultFeedbacks) {
                    if (VaultFeedbacks.hasOwnProperty(key) && VaultFeedbacks[key].id == json.error) {
                        if (VaultFeedbacks.unexpectedError === VaultFeedbacks[key]) {
                            show(VaultFeedbacks.unexpectedError.replace("{{0}}", json.message));
                        }
                        else {
                            show(VaultFeedbacks[key]);
                        }
                        if (json.hasOwnProperty("details"))
                            console.warn(json.details);
                        return;
                    }
                }
                show(VaultFeedbacks.unexpectedError.replace("{{0}}", json.message));
                if (json.hasOwnProperty("details"))
                    console.warn(json.details);
            }
        }
        async function needRetry(json) {
            if (json.hasOwnProperty("error")) {
                var handled = false;
                if (json.error === "need-device") {
                    handled = true;
                    if (await self.askForDevice())
                        return true;
                }
                if (json.error === "need-pin") {
                    handled = true;
                    if (await self.askForPin())
                        return true;
                }
                if (json.error === "need-passphrase") {
                    handled = true;
                    if (await self.askForPassphrase())
                        return true;
                }
                if (json.error === "need-passphrase-on-device") {
                    handled = true;
                    show(VaultFeedbacks.needPassphraseOnDevice);
                    self.bridge.socket.send("ask-passphrase");
                    var json = await self.bridge.waitBackendMessage();
                    if (json.hasOwnProperty("error")) {
                        showError(json);
                        return false;
                    }
                    return true;
                }

                if (!handled) {
                    showError(json);
                }
            }
            return false;
        }

        this.waitRetryPushed = function () {
            var button = $("#vault-retry");
            return new Promise(function (resolve) {
                button.click(function () {
                    // Cleanup old feedback
                    var icon = $(".vault-feedback-icon");
                    icon.removeClass();
                    icon.addClass("vault-feedback-icon");
                    var content = $(".vault-feedback-content");
                    content.html('');
                    ///////////////////
                    button.hide();
                    self.retryShowing = false;
                    resolve(true);
                });
            });
        };

        this.ensureConnectedToBackend = async function () {
            if (self.retryShowing) {
                await self.waitRetryPushed();
            }
            if (!self.bridge || self.bridge.socket.readyState !== 1) {
                $("#vault-dropdown").css("display", "none");
                show(VaultFeedbacks.vaultLoading);
                try {
                    await vault.askVaultPermission();
                } catch (ex) {
                    if (ex == vault.errors.notRunning)
                        show(VaultFeedbacks.noVault);
                    else if (ex == vault.errors.denied)
                        show(VaultFeedbacks.vaultDenied);
                    return false;
                }
                show(VaultFeedbacks.vaultGranted);
                try {
                    self.bridge = await vault.connectToBackendSocket(self.backend_uri);
                    show(VaultFeedbacks.bridgeConnected);
                } catch (ex) {
                    if (ex == vault.errors.socketNotSupported)
                        show(VaultFeedbacks.noWebsockets);
                    if (ex == vault.errors.socketError)
                        show(VaultFeedbacks.errorWebsockets);
                    return false;
                }
            }
            return true;
        };

        this.askForDisplayAddress = async function (rootedKeyPath) {
            if (!await self.ensureConnectedToBackend())
                return false;
            show(VaultFeedbacks.reviewAddress);
            self.bridge.socket.send("display-address");
            self.bridge.socket.send(rootedKeyPath);
            var json = await self.bridge.waitBackendMessage();
            if (json.hasOwnProperty("error")) {
                if (await needRetry(json))
                    return await self.askForDisplayAddress(rootedKeyPath);
                return false;
            }
            return true;
        }
        this.askForDevice = async function () {
            if (!await self.ensureConnectedToBackend())
                return false;
            show(VaultFeedbacks.fetchingDevice);
            self.bridge.socket.send("ask-device");
            var json = await self.bridge.waitBackendMessage();
            if (json.hasOwnProperty("error")) {
                showError(json);
                return false;
            }
            show(VaultFeedbacks.deviceFound.replace("{{0}}", json.model));
            return true;
        };

        this.askForXPubs = async function () {
            if (!await self.ensureConnectedToBackend())
                return false;
            self.bridge.socket.send("ask-xpub");
            var json = await self.bridge.waitBackendMessage();
            if (json.hasOwnProperty("error")) {
                if (await needRetry(json))
                    return await self.askForXPubs();
                return false;
            }
            try {
                var selectedXPubs = await self.getXpubSettings();
                self.bridge.socket.send(JSON.stringify(selectedXPubs));
                show(VaultFeedbacks.fetchingXpubs);
                json = await self.bridge.waitBackendMessage();
                if (json.hasOwnProperty("error")) {
                    if (await needRetry(json))
                        return await self.askForXPubs();
                    return false;
                }
                show(VaultFeedbacks.fetchedXpubs);
                self.xpub = json;
                return true;
            } catch (err) {
                showError({ error: true, message: err });
            }
        };

        /**
        * @returns {Promise<{addressType:string, accountNumber:number}>}
        */
        this.getXpubSettings = function () {
            show(VaultFeedbacks.askXpubs);
            $("#vault-xpub").css("display", "block");
            $("#vault-confirm").css("display", "block");
            $("#vault-confirm").text("Confirm");
            return new Promise(function (resolve, reject) {
                $("#vault-confirm").click(async function (e) {
                    e.preventDefault();
                    $("#vault-xpub").css("display", "none");
                    $("#vault-confirm").css("display", "none");
                    $(this).unbind();
                    const addressType = $("select[name=\"addressType\"]").val();
                    const accountNumber = parseInt($("input[name=\"accountNumber\"]").val());
                    if (addressType && !isNaN(accountNumber)) {
                        resolve({ addressType, accountNumber });
                    } else {
                        reject("Provide an address type and account number")
                    }
                });
            });
        };

        /**
         * @returns {Promise<string>}
         */
        this.getUserEnterPin = function () {
            show(VaultFeedbacks.needPin);
            $("#pin-input").css("display", "block");
            $("#vault-confirm").css("display", "block");
            $("#vault-confirm").text("Confirm the pin code");
            return new Promise(function (resolve, reject) {
                var pinCode = "";
                $("#vault-confirm").click(async function (e) {
                    e.preventDefault();
                    $("#pin-input").css("display", "none");
                    $("#vault-confirm").css("display", "none");
                    $(this).unbind();
                    $(".pin-button").unbind();
                    $("#pin-display-delete").unbind();
                    resolve(pinCode);
                });
                $("#pin-display-delete").click(function () {
                    pinCode = "";
                    $("#pin-display").val("");
                });
                $(".pin-button").click(function () {
                    var id = $(this).attr('id').replace("pin-", "");
                    pinCode = pinCode + id;
                    $("#pin-display").val($("#pin-display").val() + "*");
                });
            });
        };

        /**
         * @returns {Promise<string>}
         */
        this.getUserPassphrase = function () {
            show(VaultFeedbacks.needPassphrase);
            $("#passphrase-input").css("display", "block");
            $("#vault-confirm").css("display", "block");
            $("#vault-confirm").text("Confirm the passphrase");
            return new Promise(function (resolve, reject) {
                $("#vault-confirm").click(async function (e) {
                    e.preventDefault();
                    var passphrase = $("#Password").val();
                    if (passphrase !== $("#PasswordConfirmation").val()) {
                        show(VaultFeedbacks.invalidPasswordConfirmation);
                        return;
                    }
                    $("#passphrase-input").css("display", "none");
                    $("#vault-confirm").css("display", "none");
                    $(this).unbind();
                    resolve(passphrase);
                });
            });
        };

        this.askForPassphrase = async function () {
            if (!await self.ensureConnectedToBackend())
                return false;
            var passphrase = await self.getUserPassphrase();
            self.bridge.socket.send("set-passphrase");
            self.bridge.socket.send(passphrase);
            return true;
        }

        /**
         * @returns {Promise}
         */
        this.askForPin = async function () {
            if (!await self.ensureConnectedToBackend())
                return false;

            self.bridge.socket.send("ask-pin");
            var json = await self.bridge.waitBackendMessage();
            if (json.hasOwnProperty("error")) {
                if (json.error == "device-already-unlocked")
                    return true;
                if (await needRetry(json))
                    return await self.askForPin();
                return false;
            }

            var pinCode = await self.getUserEnterPin();
            self.bridge.socket.send(pinCode);
            var json = await self.bridge.waitBackendMessage();
            if (json.hasOwnProperty("error")) {
                showError(json);
                return false;
            }
            return true;
        }

        /**
         * @returns {Promise<Boolean>}
         */
        this.askSignPSBT = async function (args) {
            if (!await self.ensureConnectedToBackend())
                return false;
            show(VaultFeedbacks.signingTransaction);
            self.bridge.socket.send("ask-sign");
            var json = await self.bridge.waitBackendMessage();
            if (json.hasOwnProperty("error")) {
                if (await needRetry(json))
                    return await self.askSignPSBT(args);
                return false;
            }
            self.bridge.socket.send(JSON.stringify(args));
            json = await self.bridge.waitBackendMessage();
            if (json.hasOwnProperty("error")) {
                if (await needRetry(json))
                    return await self.askSignPSBT(args);
                return false;
            }
            self.psbt = json.psbt;
            return true;
        };

        this.closeBridge = function () {
            if (self.bridge) {
                self.bridge.close();
            }
        };
    }
    return {
        VaultFeedback: VaultFeedback,
        VaultBridgeUI: VaultBridgeUI
    };
})();
