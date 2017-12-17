
/* TAF

- Version mobile

- Réparer le décallage par timer

- Preparer les variables de l'API

- Gestion des differents evenements en fonction du status de l'invoice

- sécuriser les CDN

*/

// TODO: Vue controller... complete migrate to it for binding, animations can stay in jQuery
var checkoutCtrl = new Vue({
    el: '#checkoutCtrl',
    components: {
        qrcode: VueQr
    },
    data: {
        srvModel: srvModel
    }
})

var display = $(".timer-row__time-left"); // Timer container

// check if the Document expired
if (srvModel.expirationSeconds > 0) {

    progressStart(srvModel.maxTimeSeconds); // Progress bar
    startTimer(srvModel.expirationSeconds, display); // Timer

    if (!validateEmail(srvModel.customerEmail))
        emailForm(); // Email form Display
    else
        hideEmailForm();
}


function hideEmailForm() {
    $("[role=document]").removeClass("enter-purchaser-email");
    $("#emailAddressView").removeClass("active");
    $("placeholder-refundEmail").html(srvModel.customerEmail);

    // Remove Email mode
    $(".modal-dialog").removeClass("enter-purchaser-email");
    $("#scan").addClass("active");
}
// Email Form
// Setup Email mode
function emailForm() {

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
            })
                .fail(function (jqXHR, textStatus, errorThrown) {

                })
                .always(function () {
                    $("#emailAddressForm .input-wrapper bp-loading-button .action-button").removeClass("loading");
                });
        } else {

            $("#emailAddressForm").addClass("ng-touched ng-dirty ng-submitted ng-invalid");

        }
    })
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
    if (!$(this).is(".active")) {
        $(this).addClass("active");
    }

    if ($("#copy-tab").is(".active")) {
        $("#copy-tab").removeClass("active");
    }

    $(".payment-tabs__slider").removeClass("slide-right");

    if (!$("#scan").is(".active")) {
        $("#copy").hide();
        $("#copy").removeClass("active");

        $("#scan").show();
        $("#scan").addClass("active");
    }
});

// Main Copy tab
$("#copy-tab").click(function () {
    if (!$(this).is(".active")) {
        $(this).addClass("active");
    }

    if ($("#scan-tab").is(".active")) {
        $("#scan-tab").removeClass("active");
    }
    if (!$(".payment-tabs__slider").is("slide-right")) {
        $(".payment-tabs__slider").addClass("slide-right");
    }

    if (!($("#copy").is(".active"))) {
        $("#copy").show();
        $("#copy").addClass("active");

        $("#scan").hide();
        $("#scan").removeClass("active");
    }
});

// Payment received
// Should connect using webhook ?
// If notification received

onDataCallback(srvModel);

function onDataCallback(jsonData) {
    var newStatus = jsonData.status;

    if (newStatus === "complete" ||
        newStatus === "confirmed" ||
        newStatus === "paid") {
        if ($(".modal-dialog").hasClass("expired")) {
            $(".modal-dialog").removeClass("expired");
        }

        if (srvModel.merchantRefLink !== "") {
            $(".action-button").click(function () {
                window.location.href = srvModel.merchantRefLink;
            });
        }
        else {
            $(".action-button").hide();
        }

        $(".modal-dialog").addClass("paid");

        if ($("#scan").hasClass("active")) {
            $("#scan").removeClass("active");
        } else if ($("#copy").hasClass("active")) {
            $("#copy").removeClass("active");
        }
        $("#paid").addClass("active");
    }

    if (newStatus === "expired" || newStatus === "invalid") { //TODO: different state if the invoice is invalid (failed to confirm after timeout)
        $(".timer-row").removeClass("expiring-soon");
        $(".timer-row__message span").html("Invoice expired.");
        $(".timer-row__spinner").html("");
        $("#emailAddressView").removeClass("active");
        $(".modal-dialog").addClass("expired");
        $("#expired").addClass("active");
    }

    if (checkoutCtrl.srvModel.status !== newStatus) {
        window.parent.postMessage({ "invoiceId": srvModel.invoiceId, "status": newStatus }, "*");
    }

    // updating ui
    checkoutCtrl.srvModel = jsonData;
}

function fetchStatus() {
    var path = srvModel.serverUrl + "/i/" + srvModel.invoiceId + "/status";
    $.ajax({
        url: path,
        type: "GET"
    }).done(function (data) {
        onDataCallback(data);
    }).fail(function (jqXHR, textStatus, errorThrown) {

    });
}

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
        console.error("Error while connecting to websocket for invoice notifictions");
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

// Validate Email address
function validateEmail(email) {
    var re = /^(([^<>()\[\]\\.,;:\s@"]+(\.[^<>()\[\]\\.,;:\s@"]+)*)|(".+"))@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}])|(([a-zA-Z\-0-9]+\.)+[a-zA-Z]{2,}))$/;
    return re.test(email);
}

// Expand Line-Items
$("#buyerTotalBtcAmount").click(function () {
    $("line-items").toggleClass("expanded");
    $(".single-item-order__right__btc-price__chevron").toggleClass("expanded");
});

// Timer Countdown
function startTimer(duration, display) {
    var timer = duration, minutes, seconds;
    var timeout = setInterval(function () {
        minutes = parseInt(timer / 60, 10);
        seconds = parseInt(timer % 60, 10);

        minutes = minutes < 10 ? "0" + minutes : minutes;
        seconds = seconds < 10 ? "0" + seconds : seconds;

        display.text(minutes + ":" + seconds);

        if (--timer < 0) {
            clearInterval(timeout);
        }
    }, 1000);
}

// Progress bar
function progressStart(timerMax) {
    var end = new Date(); // Setup Time Variable, should come from server
    end.setSeconds(end.getSeconds() + srvModel.expirationSeconds);
    timerMax *= 1000; // Usually 15 minutes = 9000 second= 900000 ms
    var timeoutVal = Math.floor(timerMax / 100); // Timeout calc
    animateUpdate(); //Launch it

    function updateProgress(percentage) {
        $('.timer-row__progress-bar').css("width", percentage + "%");
    }

    function animateUpdate() {

        var now = new Date();
        var timeDiff = end.getTime() - now.getTime();
        var perc = 100 - Math.round((timeDiff / timerMax) * 100);

        if (perc === 75 && (status === "paidPartial" || status === "new")) {
            $(".timer-row").addClass("expiring-soon");
            $(".timer-row__message span").html("Invoice expiring soon ...");
            updateProgress(perc);
        }
        if (perc <= 100) {
            updateProgress(perc);
            setTimeout(animateUpdate, timeoutVal);
        }
        if (perc >= 100 && status === "expired") {
            onDataCallback(status);
        }
    }
}

// Manual Copy
// Amount
var copyAmount = new Clipboard('.manual-box__amount__value', {
    target: function () {
        var $el = $(".manual-box__amount__value");
        $el.removeClass("copy-cursor").addClass("copied");
        setTimeout(function () { $el.removeClass("copied").addClass("copy-cursor"); }, 500);
        return document.querySelector('.manual-box__amount__value span');
    }
});
// Address
var copyAddress = new Clipboard('.manual-box__address__value', {
    target: function () {
        var $elm = $(".manual-box__address__value");
        $elm.removeClass("copy-cursor").addClass("copied");
        setTimeout(function () { $elm.removeClass("copied").addClass("copy-cursor"); }, 500);
        return document.querySelector('.manual-box__address__value .manual-box__address__wrapper .manual-box__address__wrapper__value');
    }
});

// Disable enter key
$(document).keypress(
    function (event) {
        if (event.which === '13') {
            event.preventDefault();
        }
    }
);
