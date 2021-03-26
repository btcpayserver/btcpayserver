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
    return ""+
        "<script>" +
        "if(!window.btcpay){ " +
        "   var head = document.getElementsByTagName('head')[0];" +
        "   var script = document.createElement('script');" +
        "   script.src='"+esc(srvModel.urlRoot)+"modal/btcpay.js';" +
        "   script.type = 'text/javascript';" +
        "   head.append(script);" +
        "}" +
        "function onBTCPayFormSubmit(event){" +
        "    var xhttp = new XMLHttpRequest();" +
        "    xhttp.onreadystatechange = function() {" +
        "        if (this.readyState == 4 && this.status == 200) {" +
        "            if(this.status == 200 && this.responseText){" +
        "                var response = JSON.parse(this.responseText);" +
        "                window.btcpay.showInvoice(response.invoiceId);" +
        "            }" +
        "        }" +
        "    };" +
        "    xhttp.open(\"POST\", event.target.getAttribute('action'), true);" +
        "    xhttp.send(new FormData( event.target ));" +
        "}" +       
        "</script>";
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
        //Scripts
        (srvModel.useModal? getScripts(srvModel) :"") +
        // Styles
        getStyles('template-paybutton-styles') + (isSlider ? getStyles('template-slider-styles') : '') +
        // Form
        '<form method="POST" '+ ( srvModel.useModal? ' onsubmit="onBTCPayFormSubmit(event);return false" ' : '' )+' action="' + esc(srvModel.urlRoot) + actionUrl + '" class="btcpay-form btcpay-form--' + (srvModel.fitButtonInline ? 'inline' : 'block') +'">\n' +
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
        html += addInput(priceInputName, srvModel.price);
        if(allowCurrencySelection){
            html += addInput("currency", srvModel.currency);
        }
    }
    // Custom amount
    else if (isCustomAmount) {
        html += '  <div class="btcpay-custom-container">\n    <div class="btcpay-custom">\n';
        html += srvModel.simpleInput ? '' : addPlusMinusButton("-");
        html += '  ' + addInputPrice(priceInputName, srvModel.price, widthInput, "", srvModel.simpleInput ? "number": null, srvModel.min, srvModel.max, srvModel.step);
        html += srvModel.simpleInput ? '' : addPlusMinusButton("+");
        html += '    </div>\n';
        if(allowCurrencySelection) {
            html += addSelectCurrency(srvModel.currency);
        }
        html += '  </div>\n';
    }
    // Slider
    else if (isSlider) {
        html += '  <div class="btcpay-custom-container">\n';
        html += addInputPrice(priceInputName, srvModel.price, width, 'onchange="document.querySelector(\'#btcpay-input-range\').value = document.querySelector(\'#btcpay-input-price\').value"');
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

    $("#mainCode").text(html).html();
    $("#preview").html(html);
    var form = document.querySelector("#preview form");
    var url =  new URL(form.getAttribute("action"));
    var formData =   new FormData(form);
    formData.forEach((value, key) => {
        if(key !== "jsonResponse"){
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

function addPlusMinusButton(type) {
    return '      <button class="plus-minus" onclick="event.preventDefault(); var price = parseInt(document.querySelector(\'#btcpay-input-price\').value); if (\'' + type + '\' == \'-\' && (price - 1) < 1) { return; } document.querySelector(\'#btcpay-input-price\').value = parseInt(document.querySelector(\'#btcpay-input-price\').value) ' + type + ' 1;">' + type + '</button>\n';
}

function addInputPrice(name, price, widthInput, customFn, type, min, max, step) {
    return '    <input id="btcpay-input-price" name="'+name+'" type="' + (type || "text") + '" min="' + (min || 0) + '" max="' + (max || "none") + '" step="' + (step || "any") + '" value="' + price + '" style="width: ' + widthInput + ';" oninput="event.preventDefault();isNaN(event.target.value) || event.target.value <= 0 ? document.querySelector(\'#btcpay-input-price\').value = ' + price + ' : event.target.value" ' + (customFn || '') + ' />\n';
}

function addSelectCurrency(currency) {
    return '    <select name="currency">\n' +
        ['USD', 'GBP', 'EUR', 'BTC'].map(c => '      <option value="' + c + '"' + (c === currency ? ' selected' : '') + '>' + c + '</option>').join('\n') + '\n' +
    '    </select>\n'
}

function addSlider(price, min, max, step, width) {
    return '    <input class="btcpay-input-range" id="btcpay-input-range" value="' + price + '" type="range" min="' + min + '" max="' + max + '" step="' + step + '" style="width:' + width + ';margin-bottom:15px;" oninput="document.querySelector(\'#btcpay-input-price\').value = document.querySelector(\'#btcpay-input-range\').value" />\n';
}
