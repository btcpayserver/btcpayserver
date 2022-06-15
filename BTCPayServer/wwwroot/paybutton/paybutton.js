$(function () {
    inputChanges();
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

function unesc(input) {
    return ('' + input)
        .replace(/&amp;/g, '&')
        .replace(/&lt;/g, '<')
        .replace(/&gt;/g, '>')
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
    return document.getElementById(styles).innerHTML.replace(/\s{2}/g, '').trim() + '\n'
}

function getScripts(srvModel) {
    const scripts = []
    if (srvModel.useModal) {
        const modal = document.getElementById('template-modal')
        scripts.push(unesc(modal.innerHTML))
    }
    if (srvModel.buttonType == '1') {
        const priceButtons = document.getElementById('template-price-buttons')
        const priceInput = document.getElementById('template-price-input')
        scripts.push(unesc(priceButtons.innerHTML))
        scripts.push(unesc(priceInput.innerHTML))
    }
    if (srvModel.buttonType == '2') {
        const priceSlider = document.getElementById('template-price-slider')
        const priceInput = document.getElementById('template-price-input')
        scripts.push(unesc(priceSlider.innerHTML))
        scripts.push(unesc(priceInput.innerHTML))
    }
    return scripts
}

function inputChanges(event, buttonSize) {
    if (buttonSize !== null && buttonSize !== undefined) {
        srvModel.buttonSize = buttonSize;
    }

    let width = '209px';
    let height = '57px';
    let widthInput = '3em';
    if (srvModel.buttonSize === 0) {
        width = '146px';
        widthInput = '2em';
        height = '40px';
    } else if (srvModel.buttonSize === 1) {
        width = '168px';
        height = '46px';
    } else if (srvModel.buttonSize === 2) {
        width = '209px';
        height = '57px';
    }
    let actionUrl = 'api/v1/invoices';
    let priceInputName = 'price';
    let app = srvModel.appIdEndpoint? srvModel.apps.find(value => value.id === srvModel.appIdEndpoint ): null;
    let allowCurrencySelection = true;
    let allowDefaultPaymentMethodSelection = true;
    if (app) {
        if (app.appType.toLowerCase() === 'pointofsale') {
            actionUrl = `apps/${app.id}/pos`;
        } else if (app.appType.toLowerCase() === 'crowdfund') {
            actionUrl = `apps/${app.id}/crowdfund`;
        } else {
            actionUrl = 'api/v1/invoices';
            app = null;
        }

        if (actionUrl !== 'api/v1/invoices') {
            priceInputName = 'amount';
            allowCurrencySelection = false;
            allowDefaultPaymentMethodSelection = false;
            srvModel.useModal = false;
        }
    }
    
    var html =
        // Styles
        getStyles('template-paybutton-styles') + (srvModel.buttonType == '2' ? getStyles('template-slider-styles') : '') +
        // Form
        '<form method="POST"' + (srvModel.useModal ? ' onsubmit="onBTCPayFormSubmit(event);return false"' : '') + ' action="' + esc(srvModel.urlRoot) + actionUrl + '" class="btcpay-form btcpay-form--' + (srvModel.fitButtonInline ? 'inline' : 'block') +'">\n' +
            addInput("storeId", srvModel.storeId);
    
    if (app) {
        if (srvModel.orderId) html += addInput("orderId", srvModel.orderId);
        if (srvModel.serverIpn) html += addInput("notificationUrl", srvModel.serverIpn);
        if (srvModel.browserRedirect) html += addInput("redirectUrl", srvModel.browserRedirect);
        if (srvModel.appChoiceKey) html += addInput("choiceKey", srvModel.appChoiceKey);
    } else {
        if (srvModel.useModal) html += addInput("jsonResponse", true);
        if (srvModel.orderId) html += addInput("orderId", srvModel.orderId);
        if (srvModel.checkoutDesc) html += addInput("checkoutDesc", srvModel.checkoutDesc);
        if (srvModel.serverIpn) html += addInput("serverIpn", srvModel.serverIpn);
        if (srvModel.browserRedirect) html += addInput("browserRedirect", srvModel.browserRedirect);
        if (srvModel.notifyEmail) html += addInput("notifyEmail", srvModel.notifyEmail);
        if (srvModel.checkoutQueryString) html += addInput("checkoutQueryString", srvModel.checkoutQueryString);
    }

    // Fixed amount: Add price and currency as hidden inputs
    if (srvModel.buttonType == '0') {
        if (srvModel.price) html += addInput(priceInputName, srvModel.price);
        if (allowCurrencySelection) html += addInput("currency", srvModel.currency);
    }
    // Custom amount
    else if (srvModel.buttonType == '1') {
        html += '  <div class="btcpay-custom-container">\n    <div class="btcpay-custom">\n';
        html += srvModel.simpleInput ? '' : addPlusMinusButton("-", srvModel.step, srvModel.min, srvModel.max);
        html += addInputPrice(priceInputName, srvModel.price, widthInput, srvModel.min, srvModel.max, srvModel.step);
        html += srvModel.simpleInput ? '' : addPlusMinusButton("+", srvModel.step, srvModel.min, srvModel.max);
        html += '    </div>\n';
        if (allowCurrencySelection) html += addSelectCurrency(srvModel.currency);
        html += '  </div>\n';
    }
    // Slider
    else if (srvModel.buttonType == '2') {
        const step = srvModel.step === 'any' ? 1 : srvModel.step;
        const min = srvModel.min == null ? 1 : parseInt(srvModel.min);
        const max = srvModel.max == null ? null : parseInt(srvModel.max);

        html += '  <div class="btcpay-custom-container">\n';
        html += addInputPrice(priceInputName, srvModel.price, width, min, max, step, 'handleSliderChange(event);return false');
        if (allowCurrencySelection) html += addSelectCurrency(srvModel.currency);
        html += addSlider(srvModel.price, srvModel.min, srvModel.max, srvModel.step, width);
        html += '  </div>\n';
    }

    if (allowDefaultPaymentMethodSelection && srvModel.defaultPaymentMethod !== "")
    {
        html += addInput("defaultPaymentMethod", srvModel.defaultPaymentMethod)
    }
    
    html += srvModel.payButtonText
        ? `<button type="submit" class="submit" name="submit" style="min-width:${width};min-height:${height};border-radius:4px;border-style:none;background-color:#0f3b21;" title="Pay with BTCPay Server, a Self-Hosted Bitcoin Payment Processor"><span style="color:#fff">${esc(srvModel.payButtonText)}</span>\n` +
            (srvModel.payButtonImageUrl? `<img src="${esc(srvModel.payButtonImageUrl)}" style="height:${parseInt(height.replace('px', ''))}px;display:inline-block;padding:5% 0 5% 5px;vertical-align:middle;">\n` : '') +
          '</button>'
        : `  <input type="image" class="submit" name="submit" src="${esc(srvModel.payButtonImageUrl)}" style="width:${width}" alt="Pay with BTCPay Server, a Self-Hosted Bitcoin Payment Processor">\n`;
    html += '</form>';
    
    // Scripts
    const scripts = getScripts(srvModel);
    const code = html + (scripts.length ? `\n<script>\n    ${scripts.join('').trim()}\n</script>` : '')

    $("#mainCode").text(code).html();
    const preview = document.getElementById('preview');
    preview.innerHTML = html;
    scripts.forEach(snippet => {
        // script needs to be inserted as node, otherwise it won't get executed
        const script = document.createElement('script')
        script.innerHTML = snippet
        preview.appendChild(script)
    })
    const form = preview.querySelector("form");
    const formData = new FormData(form);
    let url = new URL(form.getAttribute("action"));
    formData.forEach((value, key) => {
        if (key !== "jsonResponse") {
            url.searchParams.append(key, value);
        }
    });
    url = url.href;
    
    $("#preview-link").attr('href', url);
    
    $('pre code').each(function (i, block) {
        hljs.highlightBlock(block);
    });

    return html;
}

function addInput(name, value) {
    return `  <input type="hidden" name="${esc(name)}" value="${esc(value)}" />\n`;
}

function addPlusMinusButton(type, step, min, max) {
    step = step === "any" ? 1 : step;
    min = min == null ? 1 : parseInt(min);
    max = max == null ? null : parseInt(max);
    
    return `      <button class="plus-minus" type="button" onclick="handlePlusMinus(event);return false" data-type="${type}" data-step="${step}" data-min="${min}" data-max="${max}">${type}</button>\n`;
}

function addInputPrice(name, price, widthInput, min = 0, max = 'none', step = 'any', onChange = null) {
    if (!price) price = min
    return `      <input class="btcpay-input-price" type="number" name="${esc(name)}" min="${min}" max="${max}" step="${step}" value="${price}" data-price="${price}" style="width:${widthInput};" oninput="handlePriceInput(event);return false"${onChange ? ` onchange="${onChange}"` : ''} />\n`;
}

function addSlider(price, min, max, step, width) {
    if (!price) price = min
    return `    <input type="range" class="btcpay-input-range" min="${min}" max="${max}" step="${step}" value="${price}" style="width:${width};margin-bottom:15px;" oninput="handleSliderInput(event);return false" />\n`;
}

function addSelectCurrency(currency) {
    // Remove all non-alphabet characters from input string and uppercase it for display
    const safeCurrency = currency.replace(/[^a-z]/gi, '').toUpperCase();
    const defaultCurrencies = ['USD', 'GBP', 'EUR', 'BTC'];
    const options = defaultCurrencies.map(c => `      <option value="${c}"${(c === safeCurrency ? ' selected' : '')}>${c}</option>`);
    // If user provided a currency not in our default currencies list, add it to the top of the options as a selected option
    if (defaultCurrencies.indexOf(safeCurrency) === -1) {
        options.unshift(`      <option value="${safeCurrency}" selected>${safeCurrency}</option>`)
    }

    return `    <select name="currency">\n${options.join('\n')}\n    </select>\n`
}
