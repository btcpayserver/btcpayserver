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

function getScripts(srvModel) {
    if (!srvModel.useModal) return ''
    const template = document.getElementById('template-get-scripts')
    return template.innerHTML.replace(/&amp;/g, '&')
}

function inputChanges(event, buttonSize) {
    if (buttonSize !== null && buttonSize !== undefined) {
        srvModel.buttonSize = buttonSize;
    }

    var isFixedAmount = srvModel.buttonType == 0
    var isCustomAmount = srvModel.buttonType == 1
    var isSlider = srvModel.buttonType == 2

    var width = "209px";
    var height = "57px";
    var widthInput = "3em";
    if (srvModel.buttonSize === 0) {
        width = "146px";
        widthInput = "2em";
        height = "40px";
    } else if (srvModel.buttonSize === 1) {
        width = "168px";
        height = "46px";
    } else if (srvModel.buttonSize === 2) {
        width = "209px";
        height = "57px";
    }
    var actionUrl = "api/v1/invoices";
    var priceInputName = "price";
    var app = srvModel.appIdEndpoint? srvModel.apps.find(value => value.id === srvModel.appIdEndpoint ): null;
    var allowCurrencySelection = true;
    if (app) {

        if (app.appType.toLowerCase() == "pointofsale") {
            actionUrl = "apps/" + app.id + "/pos";
        } else if (app.appType.toLowerCase() == "crowdfund") {
            actionUrl = "apps/" + app.id + "/crowdfund";
        } else {
            actionUrl = "api/v1/invoices";
            app = null;
        }

        if (actionUrl != "api/v1/invoices") {
            priceInputName = "amount";
            allowCurrencySelection = false;
            srvModel.useModal = false;
        }
    }
    
    var html =
        // Styles
        getStyles('template-paybutton-styles') + (isSlider ? getStyles('template-slider-styles') : '') +
        // Form
        '<form method="POST"' + (srvModel.useModal ? ' onsubmit="onBTCPayFormSubmit(event);return false"' : '') + ' action="' + esc(srvModel.urlRoot) + actionUrl + '" class="btcpay-form btcpay-form--' + (srvModel.fitButtonInline ? 'inline' : 'block') +'">\n' +
            addInput("storeId", srvModel.storeId);
    
    if(app){
        if (srvModel.orderId) html += addInput("orderId", srvModel.orderId);
        if (srvModel.serverIpn) html += addInput("notificationUrl", srvModel.serverIpn);
        if (srvModel.browserRedirect) html += addInput("redirectUrl", srvModel.browserRedirect);
        if (srvModel.appChoiceKey) html += addInput("choiceKey", srvModel.appChoiceKey);
        
    }else{
        if (srvModel.useModal) html += addInput("jsonResponse", true);

        if (srvModel.orderId) html += addInput("orderId", srvModel.orderId);
        if (srvModel.checkoutDesc) html += addInput("checkoutDesc", srvModel.checkoutDesc);


        if (srvModel.serverIpn) html += addInput("serverIpn", srvModel.serverIpn);

        if (srvModel.browserRedirect) html += addInput("browserRedirect", srvModel.browserRedirect);

        if (srvModel.notifyEmail) html += addInput("notifyEmail", srvModel.notifyEmail);

        if (srvModel.checkoutQueryString) html += addInput("checkoutQueryString", srvModel.checkoutQueryString);
    }

    // Fixed amount: Add price and currency as hidden inputs
    if (isFixedAmount) {
        if (srvModel.price)
            html += addInput(priceInputName, srvModel.price);
        if(allowCurrencySelection){
            html += addInput("currency", srvModel.currency);
        }
    }
    // Custom amount
    else if (isCustomAmount) {
        html += '  <div class="btcpay-custom-container">\n    <div class="btcpay-custom">\n';
        html += srvModel.simpleInput ? '' : addPlusMinusButton("-", srvModel.step, srvModel.min, srvModel.max);
        if (srvModel.price)
            html += '  ' + addInputPrice(priceInputName, srvModel.price, widthInput, "",   "number", srvModel.min, srvModel.max, srvModel.step);
        html += srvModel.simpleInput ? '' : addPlusMinusButton("+", srvModel.step, srvModel.min, srvModel.max);
        html += '    </div>\n';
        if(allowCurrencySelection) {
            html += addSelectCurrency(srvModel.currency);
        }
        html += '  </div>\n';
    }
    // Slider
    else if (isSlider) {
        var step = srvModel.step =="any"? 1: srvModel.step;
        var min = srvModel.min == null? 1: parseInt(srvModel.min);
        var max = srvModel.max == null? 1: parseInt(srvModel.max);
        var onChange = "var el=document.querySelector(\'#btcpay-input-price\'); var price = parseInt(el.value);  if(price< "+min+") { el.value = "+min+"} else if(price> "+max+") { el.value = "+max+"} document.querySelector(\'#btcpay-input-range\').value = el.value"

        html += '  <div class="btcpay-custom-container">\n';
        html += addInputPrice(priceInputName, srvModel.price, width, 'onchange= \"'+onChange+'\"');
        if(allowCurrencySelection) {
            html += addSelectCurrency(srvModel.currency);
        }
        html += addSlider(srvModel.price, srvModel.min, srvModel.max, srvModel.step, width);
        html += '  </div>\n';
    }
    
    if(!srvModel.payButtonText){
        html += '  <input type="image" class="submit" name="submit" src="' + esc(srvModel.payButtonImageUrl) + '" style="width:' + width + '" alt="Pay with BtcPay, Self-Hosted Bitcoin Payment Processor">\n';
    }else{
        var numheight = parseInt(height.replace("px", ""));
        html+= '<button type="submit" class="submit" name="submit" style="min-width:' + width + '; min-height:' + height + '; border-radius: 4px;border-style: none;background-color: #0f3b21;" alt="Pay with BtcPay, Self-Hosted Bitcoin Payment Processor"><span style="color:#fff">'+esc(srvModel.payButtonText)+'</span>\n' +
            (srvModel.payButtonImageUrl? '<img src="'+esc(srvModel.payButtonImageUrl)+'" style="height:'+numheight+'px;display:inline-block;padding: 5% 0 5% 5px;vertical-align: middle;">\n' : '')+
            '</button>'
    }
    html += '</form>';
    
    // Scripts
    var scripts = getScripts(srvModel);
    var code = html + (scripts ? `\n<script>\n        ${scripts.trim()}\n</script>` : '')

    $("#mainCode").text(code).html();
    var preview = document.getElementById('preview');
    preview.innerHTML = html;
    if (scripts) {
        // script needs to be inserted as node, otherwise it won't get executed
        var script = document.createElement('script');
        script.innerHTML = scripts
        preview.appendChild(script)
    }
    var form = preview.querySelector("form");
    var url =  new URL(form.getAttribute("action"));
    var formData =   new FormData(form);
    formData.forEach((value, key) => {
        if (key !== "jsonResponse") {
            url.searchParams.append(key, value);
        }
    });
    url = url.href;
    
    $("#preview-link").empty().append($('<a></a>').text(url).attr('href', url));
    
    $('pre code').each(function (i, block) {
        hljs.highlightBlock(block);
    });

    return html;
}

function addInput(name, value) {
    return '  <input type="hidden" name="' + esc(name) + '" value="' + esc(value) + '" />\n';
}

function addPlusMinusButton(type, step, min, max) {
    step = step =="any"? 1: step;
    min = min == null? 1: parseInt(min);
    max = max == null? 1: parseInt(max);
    var onChange = "event.preventDefault(); var el=document.querySelector(\'#btcpay-input-price\'); var price = parseInt(el.value);"
    if(type == "-"){
        onChange += " if((price - "+step+" )< "+min+") { el.value = "+min+"} else {el.value = parseInt(el.value) - "+step + " }";
    } else if(type == "+"){
        onChange += " if((price + "+step+" )> "+max+") { el.value = "+max+"} else {el.value = parseInt(el.value) + "+step + " }";
    }
    
    
    return '      <button class="plus-minus" onclick="'+onChange+'">' + type + '</button>\n';
   }

function addInputPrice(name, price, widthInput, customFn, type, min, max, step) {
    return '    <input id="btcpay-input-price" name="'+name+'" type="' + (type || "text") + '" min="' + (min || 0) + '" max="' + (max || "none") + '" step="' + (step || "any") + '" value="' + price + '" style="width: ' + widthInput + ';" oninput="event.preventDefault();isNaN(event.target.value)? document.querySelector(\'#btcpay-input-price\').value = ' + price + ' : event.target.value; if (this.value < '+min+') {this.value = '+min+'; } else if(this.value > '+max+'){  this.value = '+max+';}" ' + (customFn || '') + ' />\n';
}

function addSelectCurrency(currency) {
    // Remove all non-alphabet characters from input string and uppercase it for display
    var safeCurrency = currency.replace(/[^a-z]/gi, '').toUpperCase();
    var defaultCurrencies = ['USD', 'GBP', 'EUR', 'BTC'];
    var options = defaultCurrencies.map(c => '      <option value="' + c + '"' + (c === safeCurrency ? ' selected' : '') + '>' + c + '</option>');
    // If user provided a currency not in our default currencies list, add it to the top of the options as a selected option
    if (defaultCurrencies.indexOf(safeCurrency) === -1) {
        options.unshift('      <option value="' + safeCurrency + '" selected>' + safeCurrency + '</option>')
    }

    return '    <select name="currency">\n' +
        options.join('\n') + '\n' +
    '    </select>\n'
}

function addSlider(price, min, max, step, width) {
    return '    <input class="btcpay-input-range" id="btcpay-input-range" value="' + price + '" type="range" min="' + min + '" max="' + max + '" step="' + step + '" style="width:' + width + ';margin-bottom:15px;" oninput="document.querySelector(\'#btcpay-input-price\').value = document.querySelector(\'#btcpay-input-range\').value" />\n';
}
