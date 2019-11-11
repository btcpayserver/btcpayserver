function initLedger() {
    var ledgerDetected = false;

    var loc = window.location, new_uri;
    if (loc.protocol === "https:") {
        new_uri = "wss:";
    } else {
        new_uri = "ws:";
    }
    new_uri += "//" + loc.host;
    new_uri += loc.pathname + "/ledger/ws";

    var bridge = new ledgerwebsocket.LedgerWebSocketBridge(new_uri);

    var cryptoSelector = $("#CryptoCurrency");
    function GetSelectedCryptoCode() {
        return cryptoSelector.val();
    }

    function WriteAlert(type, message) {

    }
    function showFeedback(id) {
        $("#ledger-loading").css("display", id === "ledger-loading" ? "block" : "none");
        $("#no-ledger-info").css("display", id === "no-ledger-info" ? "block" : "none");
        $("#ledger-validate").css("display", id === "ledger-validate" ? "block" : "none");
        $(".display-when-ledger-connected").css("display", id === "ledger-info" ? "block" : "none");
    }
    function Write(prefix, type, message) {
        if (type === "error") {
            showFeedback("no-ledger-info");
        }
    }
    $("#DerivationScheme").change(function () {
        $("#KeyPath").val("");
    });
    $(".ledger-info-recommended").on("click", function (elem) {
        elem.preventDefault();

        showFeedback("ledger-validate");

        var keypath = elem.currentTarget.getAttribute("data-ledgerkeypath");
        var cryptoCode = GetSelectedCryptoCode();
        bridge.sendCommand("getxpub", "cryptoCode=" + cryptoCode + "&keypath=" + keypath)
            .then(function (result) {
                if (cryptoCode !== GetSelectedCryptoCode())
                    return;

                showFeedback("ledger-info");
                $("#DerivationScheme").val(result.derivationScheme);
                $("#RootFingerprint").val(result.rootFingerprint);
                $("#AccountKey").val(result.extPubKey);
                $("#Source").val(result.source);
                $("#DerivationSchemeFormat").val("BTCPay");
                $("#KeyPath").val(keypath);
                $(".modal").modal('hide');
                $(".hw-fields").show();
            })
            .catch(function (reason) { Write('check', 'error', reason); });
        return false;
    });

    bridge.isSupported()
        .then(function (supported) {
            if (!supported) {
                Write('hw', 'error', 'U2F or Websocket are not supported by this browser');
            }
            else {
                bridge.sendCommand('test', null, 5)
                    .catch(function (reason) {
                        if (reason.name === "TransportError")
                            reason = "Have you forgot to activate browser support in your ledger app?";
                        Write('hw', 'error', reason);
                    })
                    .then(function (result) {
                        if (!result)
                            return;
                        if (result.error) {
                            Write('hw', 'error', result.error);
                        } else {
                            Write('hw', 'success', 'Ledger detected');
                            ledgerDetected = true;
                            showFeedback("ledger-info");
                        }
                    });
            }
        });
}

$(document).ready(function () {
    var ledgerInit = false;
    $(".check-for-ledger").on("click", function () {
        if (!ledgerInit) {

            initLedger();
        }
        ledgerInit = true;
    });

    function show(id, category) {
        $("." + category).css("display", "none");
        $("#" + id).css("display", "block");
    }

    var websocketPath = $("#WebsocketPath").text();
    var loc = window.location, ws_uri;
    if (loc.protocol === "https:") {
        ws_uri = "wss:";
    } else {
        ws_uri = "ws:";
    }
    ws_uri += "//" + loc.host;
    ws_uri += websocketPath;

    function displayXPubs(xpubs) {
        $("#vault-dropdown").css("display", "block");
        $("#vault-dropdown .dropdown-item").click(function () {
            var id = $(this).attr('id').replace("vault-", "");
            var xpub = xpubs[id];
            $("#DerivationScheme").val(xpub.strategy);
            $("#RootFingerprint").val(xpubs.fingerprint);
            $("#AccountKey").val(xpub.accountKey);
            $("#Source").val("Vault");
            $("#DerivationSchemeFormat").val("BTCPay");
            $("#KeyPath").val(xpub.keyPath);
            $(".modal").modal('hide');
            $(".hw-fields").show();
        });
    }

    var vaultInit = false;
    $(".check-for-vault").on("click", async function () {
        if (vaultInit)
            return;
        vaultInit = true;

        var html = $("#VaultConnection").html();
        $("#vaultPlaceholder").html(html);

        var vaultUI = new vaultui.VaultBridgeUI(ws_uri);
        if (await vaultUI.askForXPubs()) {
            displayXPubs(vaultUI.xpubs);
        }
    });
});
