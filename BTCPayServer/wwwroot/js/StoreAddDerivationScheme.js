$(function () {
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
        $("#ledger-info").css("display", id === "ledger-info" ? "block" : "none");
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

                $("#DerivationScheme").val(result.extPubKey);
                $("#DerivationSchemeFormat").val("BTCPay");
                $("#KeyPath").val(keypath);
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
});
