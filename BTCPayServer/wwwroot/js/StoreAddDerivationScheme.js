$(function () {


    var classicDerivationInputElement = $("#DerivationScheme");
    var smartDerivationInputElement = $("#ExtPubKey");
    var smartDerivationTypeElement = $("#AddressType");
    var smartDerivationInputContainer = $("#smart-derivation-input");
    var classDerivationInputContainer = $(".classic-derivation-input");
    var derivationInputMethodElement = $("#derivation-input-method");


    function toggleClassicDerivationInput() {
        classDerivationInputContainer.toggle();
        smartDerivationInputContainer.toggle();
    }

    function setClassicDerivationInput() {
        var newVal = smartDerivationInputElement.val();
        var typeSuffix = smartDerivationTypeElement.find(":selected").data("suffix");
        newVal += typeSuffix;
        classicDerivationInputElement.val(newVal);
    }

    classicDerivationInputElement.on("input", function () {
        var newValue = $(this).val();
        var split = newValue.split("-");
        smartDerivationInputElement.val(split[0]);
        var suffix = "";
        var typeValue = "";
        if (split.length > 1) {
            suffix = "-" + split[1];
            typeValue = smartDerivationTypeElement.find("[data-suffix='" + suffix + "']").attr("value");
        }

        if (!typeValue) {
            smartDerivationTypeElement.find("[data-suffix]").each(function (i, x) {
                x = $(x);
                if (!x.data("data-suffix")) {
                    typeValue = x.val();
                }
            });
        }
        smartDerivationTypeElement.val(typeValue);
    });

    smartDerivationInputElement.on("input", setClassicDerivationInput);
    smartDerivationTypeElement.on("input", setClassicDerivationInput);

    derivationInputMethodElement.on("click", function (event) {
        event.preventDefault();
        toggleClassicDerivationInput();
    });

    var ledgerDetected = false;
    var bridge = new ledgerwebsocket.LedgerWebSocketBridge(srvModel);

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
            .catch(function (reason) {
                Write('check', 'error', reason);
            });
        return false;
    });

    var updateInfo = function () {
        if (!ledgerDetected)
            return false;
        var cryptoCode = GetSelectedCryptoCode();
        bridge.sendCommand("getxpub", "cryptoCode=" + cryptoCode)
            .catch(function (reason) {
                Write('check', 'error', reason);
            })
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
