$(function () {
    var ledgerDetected = false;
    var recommendedPubKey = "";
    var bridge = new ledgerwebsocket.LedgerWebSocketBridge(srvModel + "ws/ledger");

    function WriteAlert(type, message) {
        
    }

    function Write(prefix, type, message) {
        if (type === "error") {
            $("#no-ledger-info").css("display", "block");
            $("#ledger-info").css("display", "none");
        }
    }

    $("#ledger-info-recommended").on("click", function (elem) {
        elem.preventDefault();
        $("#DerivationScheme").val(recommendedPubKey);
        $("#DerivationSchemeFormat").val("BTCPay");
        return false;
    });

    $("#CryptoCurrency").on("change", function (elem) {
        $("#no-ledger-info").css("display", "none");
        $("#ledger-info").css("display", "none");
        updateInfo();
    });

    var updateInfo = function () {
        if (!ledgerDetected)
            return false;
        var cryptoCode = $("#CryptoCurrency").val();
        bridge.sendCommand("getxpub", "cryptoCode=" + cryptoCode)
            .catch(function (reason) { Write('check', 'error', reason); })
            .then(function (result) {
                if (result.error) {
                    Write('check', 'error', result.error);
                    return;
                }
                else {
                    Write('check', 'success', 'This store is configured to use your ledger');
                    recommendedPubKey = result.extPubKey;
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
                bridge.sendCommand('test')
                    .catch(function (reason) { Write('hw', 'error', reason); })
                    .then(function (result) {
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
