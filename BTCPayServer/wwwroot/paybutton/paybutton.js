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

    // Add price as hidden only if it's a fixed amount (srvModel.buttonType = 0)
    if (srvModel.buttonType == 0) {
        html += addinput("price", srvModel.price);
    }
    else if (srvModel.buttonType == 1) {
        html += '\n    <div style="text-align:center;width:' + width + '">';
        html += '<div>';
        html += addPlusMinusButton("-");
        html += addInputPrice(srvModel.price, widthInput, "");
        html += addPlusMinusButton("+");
        html += '</div>';
        html += addSelectCurrency();
        html += '</div>';
    }
    else if (srvModel.buttonType == 2) {
        html += '\n    <div style="text-align:center;width:' + width + '">';
        html += addInputPrice(srvModel.price, width, 'onchange="document.querySelector(\'#btcpay-input-range\').value = document.querySelector(\'#btcpay-input-price\').value"');
        html += addSelectCurrency();
        html += addSlider(srvModel.price, srvModel.min, srvModel.max, srvModel.step, width);
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
    var button = document.getElementById('template-button-plus-minus').innerHTML.trim();
    if (type === "+") {
        return button.replace(/TYPE/g, '+');
    } else {
        return button.replace(/TYPE/g, '-');
    }
}

function addInputPrice(price, widthInput, customFn) {
    var input = document.getElementById('template-input-price').innerHTML.trim();

    input = input.replace(/PRICEVALUE/g, price);
    input = input.replace("WIDTHINPUT", widthInput);

    if (customFn) {
        return input.replace("CUSTOM", customFn);
    }
    return input.replace("CUSTOM", "");
}

function addSelectCurrency() {
    return document.getElementById('template-select-currency').innerHTML.trim();
}

function addSlider(price, min, max, step, width) {
    var input = document.getElementById('template-input-slider').innerHTML.trim();
    input = input.replace("PRICE", price);
    input = input.replace("MIN", min);
    input = input.replace("MAX", max);
    input = input.replace("STEP", step);
    input = input.replace("WIDTH", width);
    return input;
}
