$(function () {
    var ledgerDetected = false;
    var bridge = new ledgerwebsocket.LedgerWebSocketBridge(srvModel.serverUrl + "ws/ledger");

    function WriteAlert(type, message) {
        $(".alert").removeClass("alert-danger");
        $(".alert").removeClass("alert-warning");
        $(".alert").removeClass("alert-success");
        $(".alert").addClass("alert-" + type);
        $(".alert").css("display", "block");
        $("#alertMessage").text(message);
    }

    function Write(prefix, type, message) {

        $("#" + prefix + "-loading").css("display", "none");
        $("#" + prefix + "-error").css("display", "none");
        $("#" + prefix + "-success").css("display", "none");

        $("#" + prefix+"-" + type).css("display", "block");

        $("." + prefix +"-label").text(message);
    }

    $("#sendform").on("submit", function (elem) {
        elem.preventDefault();

        var args = "";
        args += "cryptoCode=" + $("#cryptoCurrencies").val();
        args += "&destination=" + $("#destination-textbox").val();
        args += "&amount=" + $("#amount-textbox").val();
        args += "&feeRate=" + $("#fee-textbox").val();
        args += "&substractFees=" + $("#substract-checkbox").prop("checked");

        WriteAlert("warning", 'Please validate the transaction on your ledger');

        var confirmButton = $("#confirm-button");
        confirmButton.prop("disabled", true);
        confirmButton.addClass("disabled");

        bridge.sendCommand('sendtoaddress', args, 60 * 5 /* timeout */)
            .catch(function (reason) {
                WriteAlert("danger", reason);
                confirmButton.prop("disabled", false);
                confirmButton.removeClass("disabled");
            })
            .then(function (result) {
                confirmButton.prop("disabled", false);
                confirmButton.removeClass("disabled");
                if (result.error) {
                    WriteAlert("danger", result.error);
                } else {
                    WriteAlert("success", 'Transaction broadcasted (' + result.transactionId + ')');
                    updateInfo();
                }
            });
        return false;
    });

    $("#crypto-balance-link").on("click", function (elem) {
        elem.preventDefault();
        var val = $("#crypto-balance-link").text();
        $("#amount-textbox").val(val);
        $("#substract-checkbox").prop('checked', true);
        return false;
    });

    $("#crypto-fee-link").on("click", function (elem) {
        elem.preventDefault();
        var val = $("#crypto-fee-link").text();
        $("#fee-textbox").val(val);
        return false;
    });

    $("#cryptoCurrencies").on("change", function (elem) {
        updateInfo();
    });

    var updateInfo = function () {
        if (!ledgerDetected)
            return false;
        $(".crypto-info").css("display", "none");
        var cryptoCode = $("#cryptoCurrencies").val();
        bridge.sendCommand("getinfo", "cryptoCode=" + cryptoCode)
            .catch(function (reason) { Write('check', 'error', reason); })
            .then(function (result) {
                if (result.error) {
                    Write('check', 'error', result.error);
                    return;
                }
                else {
                    Write('check', 'success', 'This store is configured to use your ledger');
                    $(".crypto-info").css("display", "block");
                    $("#crypto-fee").text(result.recommendedSatoshiPerByte);
                    $("#crypto-balance").text(result.balance);
                    $("#crypto-code").text(cryptoCode);
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
                            $("#sendform").css("display", "block");
                            ledgerDetected = true;
                            updateInfo();
                        }
                    });
            }
        });
});
