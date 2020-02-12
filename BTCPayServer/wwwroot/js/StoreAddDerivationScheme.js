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


function getVaultUI() {
    var websocketPath = $("#WebsocketPath").text();
    var loc = window.location, ws_uri;
    if (loc.protocol === "https:") {
        ws_uri = "wss:";
    } else {
        ws_uri = "ws:";
    }
    ws_uri += "//" + loc.host;
    ws_uri += websocketPath;
    return new vaultui.VaultBridgeUI(ws_uri);
}

function showModal() {
    var html = $("#btcpayservervault_template").html();
    $("#btcpayservervault").html(html);
    html = $("#VaultConnection").html();
    $("#vaultPlaceholder").html(html);
    $('#btcpayservervault').modal();
}

async function showAddress(rootedKeyPath, address) {
    $(".showaddress").addClass("disabled");
    showModal();
    $("#btcpayservervault #displayedAddress").text(address);
    var vaultUI = getVaultUI();
    $('#btcpayservervault').on('hidden.bs.modal', function () {
        vaultUI.closeBridge();
        $(".showaddress").removeClass("disabled");
    });
    if (await vaultUI.askForDevice())
        await vaultUI.askForDisplayAddress(rootedKeyPath);
    $('#btcpayservervault').modal("hide");
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

    function displayXPubs(xpub) {
        $("#DerivationScheme").val(xpub.strategy);
        $("#RootFingerprint").val(xpub.fingerprint);
        $("#AccountKey").val(xpub.accountKey);
        $("#Source").val("Vault");
        $("#DerivationSchemeFormat").val("BTCPay");
        $("#KeyPath").val(xpub.keyPath);
        $(".modal").modal('hide');
        $(".hw-fields").show();
    }

    $(".check-for-vault").on("click", async function () {
        var vaultUI = getVaultUI();
        showModal();
        $('#btcpayservervault').on('hidden.bs.modal', function () {
            vaultUI.closeBridge();
        });
        if (await vaultUI.askForDevice() && await vaultUI.askForXPubs()) {
            displayXPubs(vaultUI.xpub);
        }
    });
});
