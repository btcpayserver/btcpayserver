$(function () {
    var destination = $("#Destination").val();
    var amount = $("#Amount").val();
    var fee = $("#FeeSatoshiPerByte").val();
    var substractFee = $("#SubstractFees").val();

    var loc = window.location, ws_uri;
    if (loc.protocol === "https:") {
        ws_uri = "wss:";
    } else {
        ws_uri = "ws:";
    }
    ws_uri += "//" + loc.host;
    ws_uri += loc.pathname + "/ws";

    var successCallback = loc.protocol + "//" + loc.host + loc.pathname + "/success";

    var ledgerDetected = false;
    var bridge = new ledgerwebsocket.LedgerWebSocketBridge(ws_uri);
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

        $("#" + prefix + "-" + type).css("display", "block");

        $("." + prefix + "-label").text(message);
    }

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


                    var args = "";
                    args += "cryptoCode=" + cryptoCode;
                    args += "&destination=" + destination;
                    args += "&amount=" + amount;
                    args += "&feeRate=" + fee;
                    args += "&substractFees=" + substractFee;

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
                                window.location.replace(successCallback + "?txid=" + result.transactionId);
                            }
                        });
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
