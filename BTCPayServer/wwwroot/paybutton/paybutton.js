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

    var width = "209px";
    var widthInput = "3em";
    if (srvModel.buttonSize === 0) {
        width = "146px";
        widthInput = "2em";
    } else if (srvModel.buttonSize === 1) {
        width = "168px";
    } else if (srvModel.buttonSize === 2) {
        width = "209px";
    }

    var html = '<form method="POST" action="' + esc(srvModel.urlRoot) + 'api/v1/invoices">';
    html += addinput("storeId", srvModel.storeId);

    // Add price as hidden only if it's a fixed amount
    if (srvModel.buttonType == 0) {
        html += addinput("price", srvModel.price);
    }
    else if (srvModel.buttonType == 1) {
        html += '\n    <div style="text-align:center;width:' + width + '">';
        html += addPlusMinusButton("-");
        html += '<input type="text" id="btcpay-input-price" name="price" value="' + srvModel.price + '" style="' +
            'border:none;background-image:none;background-color:transparent;-webkit-box-shadow:none;-moz-box-shadow:none;-webkit-appearance: none;' + // Reset css
            'width:' + widthInput + ';text-align:center;font-size:25px;margin:auto;border-radius:5px;line-height:50px;background:#fff;"' + // Custom css
            'oninput="event.preventDefault();isNaN(event.target.value) ? document.querySelector(\'#btcpay-input-price\').value = ' + srvModel.price + ' : event.target.value" />'; // Method to try if it's a number
        html += addPlusMinusButton("+");
        html += '</div>';
    }

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

function addPlusMinusButton(type) {
    var button = '<button style="cursor:pointer; font-size:25px; line-height: 25px; background: rgba(0,0,0,.1); height: 30px; width: 45px; border:none; border-radius: 60px; margin: auto;" onclick="event.preventDefault();document.querySelector(\'#btcpay-input-price\').value = parseInt(document.querySelector(\'#btcpay-input-price\').value) TYPE 1;">TYPE</button>';
    if (type === "+") {
        return button.replace(/TYPE/g, '+');
    } else {
        return button.replace(/TYPE/g, '-');
    }
}
