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

function inputChanges(event, buttonSize) {
    if (buttonSize !== null && buttonSize !== undefined) {
        srvModel.buttonSize = buttonSize;
    }

    var width = "209px";
    var widthInput = "3em";
    if (srvModel.buttonSize === 0) {
        width = "146px";
        widthInput = "2em";
    } else if (srvModel.buttonSize === 1 ) {
        width = "168px";
    } else if (srvModel.buttonSize === 2) {
        width = "209px";
    }
    var html =
        '<style>\n' +
        '  .btcpay-form { display: inline-flex; align-items: center; justify-content: center; }\n' +
        '  .btcpay-form--inline { flex-direction: row; }\n' +
        '  .btcpay-form--block { flex-direction: column; }\n' +
        '  .btcpay-form--inline .submit { margin-left: 15px; }\n' +
        '  .btcpay-form--block select { margin-bottom: 10px; }\n' +
        '  .btcpay-form .btcpay-custom { display: flex; align-items: center; justify-content: center; }\n' +
        '  .btcpay-form .plus-minus { cursor:pointer; font-size:25px; line-height: 25px; background: rgba(0,0,0,.1); height: 30px; width: 45px; border:none; border-radius: 60px; margin: auto; }\n' +
        '  .btcpay-form select { -moz-appearance: none; -webkit-appearance: none; appearance: none; background: transparent; border:1px solid transparent; display: block; padding: 1px; margin-left: auto; margin-right: auto; font-size: 11px; cursor: pointer; }\n' +
        '  .btcpay-form select:hover { border-color: #ccc; }\n' +
        '  #btcpay-input-price { border: none; box-shadow: none; -mox-appearance: none; -webkit-appearance: none; text-align: center; font-size: 25px; margin: auto; border-radius: 5px; line-height: 35px; background: #fff; }\n' +
        '</style>\n' +
        '<form method="POST" action="' + esc(srvModel.urlRoot) + 'api/v1/invoices" class="btcpay-form btcpay-form--' + (srvModel.fitButtonInline ? 'inline' : 'block') +'">\n' +
            addInput("storeId", srvModel.storeId);

    if (srvModel.checkoutDesc) html += addInput("checkoutDesc", srvModel.checkoutDesc);

    if (srvModel.orderId) html += addInput("orderId", srvModel.orderId);

    if (srvModel.serverIpn) html += addInput("serverIpn", srvModel.serverIpn);

    if (srvModel.browserRedirect) html += addInput("browserRedirect", srvModel.browserRedirect);

    if (srvModel.notifyEmail) html += addInput("notifyEmail", srvModel.notifyEmail);

    if (srvModel.checkoutQueryString) html += addInput("checkoutQueryString", srvModel.checkoutQueryString);

    // Fixed amount: Add price and currency as hidden inputs
    if (srvModel.buttonType == 0) {
        html += addInput("price", srvModel.price);
        html += addInput("currency", srvModel.currency);
    }
    // Custom amount
    else if (srvModel.buttonType == 1) {
        html += '  <div>\n    <div class="btcpay-custom">\n';

        if (!srvModel.simpleInput) html += addPlusMinusButton("-");

        html += '  ' + addInputPrice(srvModel.price, widthInput, "", srvModel.simpleInput? "number": null, srvModel.min, srvModel.max, srvModel.step);

        if (!srvModel.simpleInput) html += addPlusMinusButton("+");

        html += '    </div>\n';
        html += addSelectCurrency(srvModel.currency);
        html += '  </div>\n';
    }
    // Slider
    else if (srvModel.buttonType == 2) {
        html += '  <div>\n';
        html += addInputPrice(srvModel.price, width, 'onchange="document.querySelector(\'#btcpay-input-range\').value = document.querySelector(\'#btcpay-input-price\').value"');
        html += addSelectCurrency(srvModel.currency);
        html += addSlider(srvModel.price, srvModel.min, srvModel.max, srvModel.step, width);
        html += '  </div>\n';
    }

    html += '  <input type="image" class="submit" name="submit" src="' + esc(srvModel.payButtonImageUrl) + '" style="width:' + width + '" alt="Pay with BtcPay, Self-Hosted Bitcoin Payment Processor">';

    html += '\n</form>';

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
    var input = document.getElementById('template-input-slider').innerHTML.trim();
    input = input.replace("PRICE", price);
    input = input.replace("MIN", min);
    input = input.replace("MAX", max);
    input = input.replace("STEP", step);
    input = input.replace("WIDTH", width);
    return '    ' + input + '\n';
}
