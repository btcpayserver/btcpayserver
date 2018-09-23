$(function () {
    inputChanges();

    // Clipboard Copy
    new Clipboard('#copyCode', {
        text: function (trigger) {
            $(".copyLabelPopup").show().delay(1000).fadeOut(500);
            return inputChanges();
        }
    });
});

function esc(input) {
    return ('' + input) /* Forces the conversion to string. */
        .replace(/&/g, '&amp;') /* This MUST be the 1st replacement. */
        .replace(/'/g, '&apos;') /* The 4 other predefined entities, required. */
        .replace(/"/g, '&quot;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        /*
        You may add other replacements here for HTML only 
        (but it's not necessary).
        Or for XML, only if the named entities are defined in its DTD.
        */
    ;
}

Vue.use(VeeValidate);
const dictionary = {
    en: {
        attributes: {
            price: 'Price', checkoutDesc: 'Checkout Description', orderId: 'Order Id',
            serverIpn: 'Server IPN', notifyEmail: 'Send Email Notifications', browserRedirect: 'Browser Redirect',
            payButtonImageUrl: "Pay Button Image Url"
        }
    }
};
VeeValidate.Validator.localize(dictionary);

function inputChanges(event, buttonSize) {
    if (buttonSize !== null && buttonSize !== undefined) {
        srvModel.buttonSize = buttonSize;
    }

    var html = '<form method="POST" action="' + esc(srvModel.urlRoot) + 'api/v1/invoices">';
    html += addinput("storeId", srvModel.storeId);
    html += addinput("price", srvModel.price);
    if (srvModel.currency) {
        html += addinput("currency", srvModel.currency);
    }
    if (srvModel.checkoutDesc) {
        html += addinput("checkoutDesc", srvModel.checkoutDesc);
    }
    if (srvModel.orderId) {
        html += addinput("orderId", srvModel.orderId);
    }

    if (srvModel.serverIpn) {
        html += addinput("serverIpn", srvModel.serverIpn);
    }
    if (srvModel.browserRedirect) {
        html += addinput("browserRedirect", srvModel.browserRedirect);
    }
    if (srvModel.notifyEmail) {
        html += addinput("notifyEmail", srvModel.notifyEmail);
    }

    var width = "209px";
    if (srvModel.buttonSize === 0) {
        width = "146px";
    } else if (srvModel.buttonSize === 1) {
        width = "168px";
    } else if (srvModel.buttonSize === 2) {
        width = "209px";
    }
    html += '\n    <input type="image" src="' + esc(srvModel.payButtonImageUrl) + '" name="submit" style="width:' + width +
        '" alt="Pay with BtcPay, Self-Hosted Bitcoin Payment Processor">';

    html += '\n</form>';

    $("#mainCode").text(html).html();
    $("#preview").html(html);

    $('pre code').each(function (i, block) {
        hljs.highlightBlock(block);
    });

    return html;
}

function addinput(name, value) {
    var html = '\n    <input type="hidden" name="' + esc(name) + '" value="' + esc(value) + '" />';
    return html;
}

