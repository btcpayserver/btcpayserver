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

    function Write(prefix, type, message) {
        if (type === "error") {
            $("#no-ledger-info").css("display", "block");
            $("#ledger-in   fo").css("display", "none");
        }
    }

    $(".ledger-info-recommended").on("click", function (elem) {
        elem.preventDefault();
        var account = elem.currentTarget.getAttribute("data-ledgeraccount");
        var cryptoCode = GetSelectedCryptoCode();
        bridge.sendCommand("getxpub", "cryptoCode=" + cryptoCode + "&account=" + account)
            .then(function (result) {
                if (cryptoCode !== GetSelectedCryptoCode())
                    return;
                $("#DerivationScheme").val(result.extPubKey);
                $("#DerivationSchemeFormat").val("BTCPay");
            })
            .catch(function (reason) { Write('check', 'error', reason); });
        return false;
    });

    var updateInfo = function () {
        if (!ledgerDetected)
            return false;
        var cryptoCode = GetSelectedCryptoCode();
        bridge.sendCommand("getxpub", "cryptoCode=" + cryptoCode)
            .catch(function (reason) { Write('check', 'error', reason); })
            .then(function (result) {
                if (!result)
                    return;
                if (cryptoCode !== GetSelectedCryptoCode())
                    return;
                if (result.error) {
                    Write('check', 'error', result.error);
                    return;
                }
                else {
                    Write('check', 'success', 'This store is configured to use your ledger');
                    $("#no-ledger-info").css("display", "none");
                    $("#ledger-info").css("display", "block");
                }
            });
    };

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
                            updateInfo();
                        }
                    });
            }
        });
});
