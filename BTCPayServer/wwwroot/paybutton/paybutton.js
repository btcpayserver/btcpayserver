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
var dictionary = {
    en: {
        attributes: {
            price: 'Price', checkoutDesc: 'Checkout Description', orderId: 'Order Id',
            serverIpn: 'Server IPN', notifyEmail: 'Send Email Notifications', browserRedirect: 'Browser Redirect',
            payButtonImageUrl: "Pay Button Image Url"
        }
    }
};
VeeValidate.Validator.localize(dictionary);

function getStyles (styles) {
    return document.getElementById(styles).innerHTML.trim().replace(/\s{2}/g, '') + '\n'
}

function inputChanges(event, buttonSize) {
    if (buttonSize !== null && buttonSize !== undefined) {
        srvModel.buttonSize = buttonSize;
    }

    var isFixedAmount = srvModel.buttonType == 0
    var isCustomAmount = srvModel.buttonType == 1
    var isSlider = srvModel.buttonType == 2

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

    var html =
        // Styles
        getStyles('template-paybutton-styles') + (isSlider ? getStyles('template-slider-styles') : '') +
        // Form
        '<form method="POST" action="' + esc(srvModel.urlRoot) + 'api/v1/invoices" class="btcpay-form btcpay-form--' + (srvModel.fitButtonInline ? 'inline' : 'block') +'">\n' +
            addInput("storeId", srvModel.storeId);

    if (srvModel.checkoutDesc) html += addInput("checkoutDesc", srvModel.checkoutDesc);

    if (srvModel.orderId) html += addInput("orderId", srvModel.orderId);

    if (srvModel.serverIpn) html += addInput("serverIpn", srvModel.serverIpn);

    if (srvModel.browserRedirect) html += addInput("browserRedirect", srvModel.browserRedirect);

    if (srvModel.notifyEmail) html += addInput("notifyEmail", srvModel.notifyEmail);

    if (srvModel.checkoutQueryString) html += addInput("checkoutQueryString", srvModel.checkoutQueryString);

    // Fixed amount: Add price and currency as hidden inputs
    if (isFixedAmount) {
        html += addInput("price", srvModel.price);
        html += addInput("currency", srvModel.currency);
    }
    // Custom amount
    else if (isCustomAmount) {
        html += '  <div>\n    <div class="btcpay-custom">\n';
        html += srvModel.simpleInput ? '' : addPlusMinusButton("-");
        html += '  ' + addInputPrice(srvModel.price, widthInput, "", srvModel.simpleInput ? "number": null, srvModel.min, srvModel.max, srvModel.step);
        html += srvModel.simpleInput ? '' : addPlusMinusButton("+");
        html += '    </div>\n';
        html += addSelectCurrency(srvModel.currency);
        html += '  </div>\n';
    }
    // Slider
    else if (isSlider) {
        html += '  <div>\n';
        html += addInputPrice(srvModel.price, width, 'onchange="document.querySelector(\'#btcpay-input-range\').value = document.querySelector(\'#btcpay-input-price\').value"');
        html += addSelectCurrency(srvModel.currency);
        html += addSlider(srvModel.price, srvModel.min, srvModel.max, srvModel.step, width);
        html += '  </div>\n';
    }

    html += '  <input type="image" class="submit" name="submit" src="' + esc(srvModel.payButtonImageUrl) + '" style="width:' + width + '" alt="Pay with BtcPay, Self-Hosted Bitcoin Payment Processor">\n';
    html += '</form>';

    $("#mainCode").text(html).html();
    $("#preview").html(html);

    $('pre code').each(function (i, block) {
        hljs.highlightBlock(block);
    });

    return html;
}

function addInput(name, value) {
    return '  <input type="hidden" name="' + esc(name) + '" value="' + esc(value) + '" />\n';
}

function addPlusMinusButton(type) {
    return '      <button class="plus-minus" onclick="event.preventDefault(); var price = parseInt(document.querySelector(\'#btcpay-input-price\').value); if (\'' + type + '\' == \'-\' && (price - 1) < 1) { return; } document.querySelector(\'#btcpay-input-price\').value = parseInt(document.querySelector(\'#btcpay-input-price\').value) ' + type + ' 1;">' + type + '</button>\n';
}

function addInputPrice(price, widthInput, customFn, type, min, max, step) {
    return '    <input id="btcpay-input-price" name="price" type="' + (type || "text") + '" min="' + (min || 0) + '" max="' + (max || "none") + '" step="' + (step || "any") + '" value="' + price + '" style="width: ' + widthInput + ';" oninput="event.preventDefault();isNaN(event.target.value) || event.target.value <= 0 ? document.querySelector(\'#btcpay-input-price\').value = ' + price + ' : event.target.value" ' + (customFn || '') + ' />\n';
}

function addSelectCurrency(currency) {
    return '    <select name="currency">\n' +
        ['USD', 'GBP', 'EUR', 'BTC'].map(c => '      <option value="' + c + '"' + (c === currency ? ' selected' : '') + '>' + c + '</option>').join('\n') + '\n' +
    '    </select>\n'
}

function addSlider(price, min, max, step, width) {
    return '    <input class="btcpay-input-range" id="btcpay-input-range" value="' + price + '" type="range" min="' + min + '" max="' + max + '" step="' + step + '" style="width:' + width + ';margin-bottom:15px;" oninput="document.querySelector(\'#btcpay-input-price\').value = document.querySelector(\'#btcpay-input-range\').value" />\n';
}
