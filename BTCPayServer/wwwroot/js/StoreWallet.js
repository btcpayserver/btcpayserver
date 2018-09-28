
function updateFiatValue() {
    if (srvModel.rate !== null) {
        var fiatValue = $("#fiatValue");
        fiatValue.css("display", "inline");
        var amountValue = parseFloat($("#amount-textbox").val());
        if (!isNaN(amountValue)) {
            fiatValue.text("= " + (srvModel.rate * amountValue).toFixed(srvModel.divisibility) + " " + srvModel.fiat);
        }
    }
}
$(function () {
    var ledgerDetected = false;
    var bridge = new ledgerwebsocket.LedgerWebSocketBridge(srvModel.serverUrl);
    var recommendedFees = "";
    var recommendedBalance = "";
    var cryptoCode = $("#cryptoCode").val();

    function WriteAlert(type, message) {
        $("#walletAlert").removeClass("alert-danger");
        $("#walletAlert").removeClass("alert-warning");
        $("#walletAlert").removeClass("alert-success");
        $("#walletAlert").addClass("alert-" + type);
        $("#walletAlert").css("display", "block");
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

        if ($("#amount-textbox").val() === "") {
            $("#amount-textbox").val(recommendedBalance);
            $("#substract-checkbox").prop("checked", true);
        }

        if ($("#fee-textbox").val() === "") {
            $("#fee-textbox").val(recommendedFees);
        }

        var args = "";
        args += "cryptoCode=" + cryptoCode;
        args += "&destination=" + $("#destination-textbox").val();
        args += "&amount=" + $("#amount-textbox").val();
        args += "&feeRate=" + $("#fee-textbox").val();
        args += "&substractFees=" + $("#substract-checkbox").prop("checked");

        WriteAlert("warning", 'Please validate the transaction on your ledger');

        var confirmButton = $("#confirm-button");
        confirmButton.prop("disabled", true);
        confirmButton.addClass("disabled");

        bridge.sendCommand('sendtoaddress', args, 60 * 10 /* timeout */)
            .catch(function (reason) {
                WriteAlert("danger", reason);
                confirmButton.prop("disabled", false);
                confirmButton.removeClass("disabled");
            })
            .then(function (result) {
                if (!result)
                    return;
                confirmButton.prop("disabled", false);
                confirmButton.removeClass("disabled");
                if (result.error) {
                    WriteAlert("danger", result.error);
                } else {
                    WriteAlert("success", 'Transaction broadcasted (' + result.transactionId + ')');
                    $("#fee-textbox").val("");
                    $("#amount-textbox").val("");
                    $("#destination-textbox").val("");
                    $("#substract-checkbox").prop("checked", false);
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

    var updateInfo = function () {
        if (!ledgerDetected)
            return false;
        $(".crypto-info").css("display", "none");
        bridge.sendCommand("getinfo", "cryptoCode=" + cryptoCode)
            .catch(function (reason) { Write('check', 'error', reason); })
            .then(function (result) {
                if (!result)
                    return;
                if (result.error) {
                    Write('check', 'error', result.error);
                    return;
                }
                else {
                    Write('check', 'success', 'This store is configured to use your ledger');
                    $(".crypto-info").css("display", "block");
                    recommendedFees = result.recommendedSatoshiPerByte;
                    recommendedBalance = result.balance;
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
                bridge.sendCommand('test', null, 5)
                    .catch(function (reason)
                    {
                        if (reason.name === "TransportError")
                            reason = "Are you running the ledger app with version equals or above 1.2.4?";
                        Write('hw', 'error', reason);
                    })
                    .then(function (result) {
                        if (!result)
                            return;
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
