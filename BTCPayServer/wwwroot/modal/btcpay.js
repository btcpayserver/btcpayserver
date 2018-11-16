/* jshint browser: true, strict: false, maxlen: false, maxstatements: false */
(function () {
    var supportsCurrentScript = ("currentScript" in document);
    var thisScript = "";
    if (supportsCurrentScript) {
        thisScript = document.currentScript.src;
    }
    else {
        for (var i = 0; i < document.scripts.length; i++) {
            var script = document.scripts[i];
            if (script.src.endsWith("btcpay.js")) {
                thisScript = script.src;
            }
        }
    }

    function warn() {
        if (window.console && window.console.warn) {
            window.console.warn.apply(window.console, arguments);
        }
    }

    if (window.btcpay) {
        warn('btcpay.js attempted to initialize more than once.');
        return;
    }

    var iframe = document.createElement('iframe');
    iframe.name = 'btcpay';
    iframe.class = 'btcpay';
    iframe.setAttribute('allowtransparency', 'true');
    iframe.style.display = 'none';
    iframe.style.border = 0;
    iframe.style.position = 'fixed';
    iframe.style.top = 0;
    iframe.style.left = 0;
    iframe.style.height = '100%';
    iframe.style.width = '100%';
    iframe.style.zIndex = '2000';

    var origin = 'http://slack.btcpayserver.org join us there, and initialize this with your origin url through setApiUrlPrefix';
    if (thisScript.endsWith("/modal/btcpay.js")) {
        // We can't just take the domain as btcpay can run under a sub path with RootPath
        origin = thisScript.substr(0, thisScript.length - "/modal/btcpay.js".length);
    }
    // urlPrefix should be site root without trailing slash
    function setApiUrlPrefix(urlPrefix) {
        origin = stripTrailingSlashes(urlPrefix);
    }
    function stripTrailingSlashes(site) {
        return site.replace(/\/+$/, "");
    } 

    var onModalWillEnterMethod = function () { };
    var onModalWillLeaveMethod = function () { };

    function showFrame() {
        if (window.document.getElementsByName('btcpay').length === 0) {
            window.document.body.appendChild(iframe);
        }
        onModalWillEnterMethod();
        iframe.style.display = 'block';
    }

    function hideFrame() {
        onModalWillLeaveMethod();
        iframe.style.display = 'none';
        iframe = window.document.body.removeChild(iframe);
    }

    function onModalWillEnter(customOnModalWillEnter) {
        onModalWillEnterMethod = customOnModalWillEnter;
    }

    function onModalWillLeave(customOnModalWillLeave) {
        onModalWillLeaveMethod = customOnModalWillLeave;
    }

    function receiveMessage(event) {
        var uri;

        if (origin !== event.origin) {
            return;
        }
        if (event.data === 'close') {
            hideFrame();
        } else if (event.data === 'loaded') {
            showFrame();
        } else if (event.data && event.data.open) {
            uri = event.data.open;
            if (uri.indexOf('bitcoin:') === 0) {
                window.location = uri;
            }
        } else if (event.data && event.data.mailto) {
            uri = event.data.mailto;
            if (uri.indexOf('mailto:') === 0) {
                window.location = uri;
            }
        }
    }

    function showInvoice(invoiceId, params) {
        window.document.body.appendChild(iframe);
        var invoiceUrl = origin + '/invoice?id=' + invoiceId + '&view=modal';
        if (params && params.animateEntrance === false) {
            invoiceUrl += '&animateEntrance=false';
        }
        iframe.src = invoiceUrl;
    }

    function setButtonListeners() {
        var buttons = window.document.querySelectorAll('[data-btcpay-button]');
        for (var i = 0; i < buttons.length; i++) {
            var b = buttons[0];
            b.addEventListener('submit', showFrame);
        }
    }

    window.addEventListener('load', function load() {
        window.removeEventListener('load', load);
    });

    window.addEventListener('message', receiveMessage, false);
    setButtonListeners();

    window.btcpay = {
        showFrame: showFrame,
        hideFrame: hideFrame,
        showInvoice: showInvoice,
        onModalWillEnter: onModalWillEnter,
        onModalWillLeave: onModalWillLeave,
        setApiUrlPrefix: setApiUrlPrefix
    };

})();
