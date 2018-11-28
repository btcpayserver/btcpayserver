// TODO: Refactor... switch from jQuery to Vue.js
// public methods
function resetTabsSlider() {
    $("#tabsSlider").removeClass("slide-copy");
    $("#tabsSlider").removeClass("slide-altcoins");

    $("#scan-tab").removeClass("active");
    $("#copy-tab").removeClass("active");
    $("#altcoins-tab").removeClass("active");

    $("#copy").hide();
    $("#copy").removeClass("active");

    $("#scan").hide();
    $("#scan").removeClass("active");

    $("#altcoins").hide();
    $("#altcoins").removeClass("active");

    closePaymentMethodDialog(null);
}

function changeCurrency(currency) {
    if (currency !== null && srvModel.paymentMethodId !== currency) {
        $(".payment__currencies").hide();
        $(".payment__spinner").show();
        checkoutCtrl.scanDisplayQr = "";
        srvModel.paymentMethodId = currency;
        fetchStatus();
    }
    return false;
}

function onDataCallback(jsonData) {
    var newStatus = jsonData.status;

    if (newStatus === "complete" ||
        newStatus === "confirmed" ||
        newStatus === "paid") {
        if ($(".modal-dialog").hasClass("expired")) {
            $(".modal-dialog").removeClass("expired");
            $("#expired").removeClass("active");
        }

        $(".modal-dialog").addClass("paid");

        resetTabsSlider();
        $("#paid").addClass("active");
    }

    if (newStatus === "expired" || newStatus === "invalid") { //TODO: different state if the invoice is invalid (failed to confirm after timeout)
        if ($(".modal-dialog").hasClass("paid")) {
            $(".modal-dialog").removeClass("paid");
            $("#paid").removeClass("active");
        }

        $(".timer-row").removeClass("expiring-soon");
        $(".timer-row__spinner").html("");
        $("#emailAddressView").removeClass("active");
        $(".modal-dialog").addClass("expired");
        $("#expired").addClass("active");

        resetTabsSlider();
    }

    if (checkoutCtrl.srvModel.status !== newStatus) {
        window.parent.postMessage({ "invoiceId": srvModel.invoiceId, "status": newStatus }, "*");
    }

    // restoring qr code view only when currency is switched
    if (jsonData.paymentMethodId === srvModel.paymentMethodId &&
        checkoutCtrl.scanDisplayQr === "") {
        checkoutCtrl.scanDisplayQr = jsonData.invoiceBitcoinUrlQR;
    }

    if (jsonData.paymentMethodId === srvModel.paymentMethodId) {
        $(".payment__currencies").show();
        $(".payment__spinner").hide();
    }

    if (jsonData.isLightning && checkoutCtrl.lndModel === null) {
        var lndModel = {
            toggle: 0
        };

        checkoutCtrl.lndModel = lndModel;
    }

    if (!jsonData.isLightning) {
        checkoutCtrl.lndModel = null;
    }

    // updating ui
    checkoutCtrl.srvModel = jsonData;
}

function fetchStatus() {
    var path = srvModel.serverUrl + "/i/" + srvModel.invoiceId + "/" + srvModel.paymentMethodId + "/status";
    $.ajax({
        url: path,
        type: "GET",
        cache: false
    }).done(function (data) {
        onDataCallback(data);
    }).fail(function (jqXHR, textStatus, errorThrown) {

    });
}

function lndToggleBolt11() {
    checkoutCtrl.lndModel.toggle = 0;
    checkoutCtrl.scanDisplayQr = checkoutCtrl.srvModel.invoiceBitcoinUrlQR;
}

function lndToggleNode() {
    checkoutCtrl.lndModel.toggle = 1;
    checkoutCtrl.scanDisplayQr = checkoutCtrl.srvModel.peerInfo;
}

// private methods
$(document).ready(function () {
    // initialize
    onDataCallback(srvModel);

    // check if the Document expired
    if (srvModel.expirationSeconds > 0) {
        progressStart(srvModel.maxTimeSeconds); // Progress bar

        if (srvModel.requiresRefundEmail && !validateEmail(srvModel.customerEmail))
            showEmailForm();
        else
            hideEmailForm();
    }

    $(".close-action").on("click", function () {
        $("invoice").fadeOut(300, function () {
            window.parent.postMessage("close", "*");
        });
    });

    window.parent.postMessage("loaded", "*");
    jQuery("invoice").fadeOut(0);
    jQuery("invoice").fadeIn(300);

    // eof initialize

    // FUNCTIONS
    function hideEmailForm() {
        $("#emailAddressView").removeClass("active");
        $("placeholder-refundEmail").html(srvModel.customerEmail);

        // Remove Email mode
        $(".modal-dialog").removeClass("enter-purchaser-email");
        $("#scan").addClass("active");
    }
    // Email Form
    // Setup Email mode
    function showEmailForm() {
        $(".modal-dialog").addClass("enter-purchaser-email");

        $("#emailAddressForm .action-button").click(function () {
            var emailAddress = $("#emailAddressFormInput").val();
            if (validateEmail(emailAddress)) {
                $("#emailAddressForm .input-wrapper bp-loading-button .action-button").addClass("loading");
                // Push the email to a server, once the reception is confirmed move on
                srvModel.customerEmail = emailAddress;

                var path = srvModel.serverUrl + "/i/" + srvModel.invoiceId + "/UpdateCustomer";

                $.ajax({
                    url: path,
                    type: "POST",
                    data: JSON.stringify({ Email: srvModel.customerEmail }),
                    contentType: "application/json; charset=utf-8"
                }).done(function () {
                    hideEmailForm();
                }).fail(function (jqXHR, textStatus, errorThrown) {

                })
                    .always(function () {
                        $("#emailAddressForm .input-wrapper bp-loading-button .action-button").removeClass("loading");
                    });
            } else {
                $("#emailAddressForm").addClass("ng-touched ng-dirty ng-submitted ng-invalid");
            }

            return false;
        });
    }

    // Validate Email address
    function validateEmail(email) {
        var re = /^(([^<>()\[\]\\.,;:\s@"]+(\.[^<>()\[\]\\.,;:\s@"]+)*)|(".+"))@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}])|(([a-zA-Z\-0-9]+\.)+[a-zA-Z]{2,}))$/;
        return re.test(email);
    }

    /* =============== Even listeners =============== */

    // Email
    $("#emailAddressFormInput").change(function () {
        if ($("#emailAddressForm").hasClass("ng-submitted")) {
            $("#emailAddressForm").removeClass("ng-submitted");
        }
    });


    // Scan/Copy Transitions
    // Scan Tab
    $("#scan-tab").click(function () {
        resetTabsSlider();
        activateTab("#scan");
    });

    // Copy tab
    $("#copy-tab").click(function () {
        resetTabsSlider();
        activateTab("#copy");

        $("#tabsSlider").addClass("slide-copy");
    });

    // Altcoins tab
    $("#altcoins-tab").click(function () {
        resetTabsSlider();
        activateTab("#altcoins");

        $("#tabsSlider").addClass("slide-altcoins");
    });

    function activateTab(senderName) {
        $(senderName + "-tab").addClass("active");

        $(senderName).show();
        $(senderName).addClass("active");
    }

    // Payment received
    // Should connect using webhook ?
    // If notification received

    var supportsWebSockets = 'WebSocket' in window && window.WebSocket.CLOSING === 2;
    if (supportsWebSockets) {
        var path = srvModel.serverUrl + "/i/" + srvModel.invoiceId + "/status/ws";
        path = path.replace("https://", "wss://");
        path = path.replace("http://", "ws://");
        try {
            var socket = new WebSocket(path);
            socket.onmessage = function (e) {
                fetchStatus();
            };
        }
        catch (e) {
            console.error("Error while connecting to websocket for invoice notifications");
        }
    }

    var watcher = setInterval(function () {
        fetchStatus();
    }, 2000);

    $(".menu__item").click(function () {
        $(".menu__scroll .menu__item").removeClass("selected");
        $(this).addClass("selected");
        language();
        $(".selector span").text($(".selected").text());
        // function to load contents in different language should go there
    });

    // Expand Line-Items
    var lineItemsExpanded = false;
    $(".buyerTotalLine").click(function () {
        $("line-items").toggleClass("expanded");
        lineItemsExpanded ? $("line-items").slideUp() : $("line-items").slideDown();
        lineItemsExpanded = !lineItemsExpanded;

        $(".buyerTotalLine").toggleClass("expanded");
        $(".single-item-order__right__btc-price__chevron").toggleClass("expanded");
    });

    // Timer Countdown && Progress bar
    function progressStart(timerMax) {
        var end = new Date(); // Setup Time Variable, should come from server
        end.setSeconds(end.getSeconds() + srvModel.expirationSeconds);
        timerMax *= 1000; // Usually 15 minutes = 9000 second= 900000 ms
        animateUpdate(); //Launch it

        function animateUpdate() {
            var now = new Date();
            var timeDiff = end.getTime() - now.getTime();
            var perc = 100 - Math.round(timeDiff / timerMax * 100);
            var status = checkoutCtrl.srvModel.status;

            updateTimer(timeDiff / 1000);

            if (perc === 75 && (status === "paidPartial" || status === "new")) {
                $(".timer-row").addClass("expiring-soon");
                checkoutCtrl.expiringSoon = true;
                updateProgress(perc);
            }
            if (perc <= 100) {
                updateProgress(perc);

                var timeoutVal = 300; // Timeout calc
                setTimeout(animateUpdate, timeoutVal);
            }

            //if (perc >= 100 && status === "expired") {
            //    onDataCallback(status);
            //}
        }

        function updateProgress(percentage) {
            $('.timer-row__progress-bar').css("width", percentage + "%");
        }

        function updateTimer(timer) {
            var display = $(".timer-row__time-left");
            if (timer >= 0) {
                var minutes = parseInt(timer / 60, 10);
                minutes = minutes < 10 ? "0" + minutes : minutes;

                var seconds = parseInt(timer % 60, 10);
                seconds = seconds < 10 ? "0" + seconds : seconds;

                display.text(minutes + ":" + seconds);
            } else {
                display.text("00:00");
            }
        }
    }

    // Clipboard Copy
    var copySpan = new Clipboard('._copySpan', {
        target: function (trigger) {
            return copyElement(trigger, 0, 65).firstChild;
        }
    });
    var copyInput = new Clipboard('._copyInput', {
        target: function (trigger) {
            return copyElement(trigger, 4, 65).firstChild;
        }
    });

    function copyElement(trigger, popupLeftModifier, popupTopModifier) {
        var elm = $(trigger);
        var position = elm.offset();
        position.top -= popupLeftModifier;
        position.left += (elm.width() / 2) - popupTopModifier;
        $(".copyLabelPopup").css(position).addClass("copied");

        elm.removeClass("copy-cursor").addClass("clipboardCopied");
        setTimeout(clearSelection, 100);
        setTimeout(function () {
            elm.removeClass("clipboardCopied").addClass("copy-cursor");
            $(".copyLabelPopup").removeClass("copied");
        }, 1000);
        return trigger;
    }
    function clearSelection() {
        if (window.getSelection) { window.getSelection().removeAllRanges(); }
        else if (document.selection) { document.selection.empty(); }
    }
    // EOF Copy

    // Disable enter key
    $(document).keypress(
        function (event) {
            if (event.which === '13') {
                event.preventDefault();
            }
        }
    );

});
