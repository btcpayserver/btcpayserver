$(function () {
    inputChanges();

    // Clipboard Copy
    new Clipboard('#copyCode', {
        text: function (trigger) {
            $(".copyLabelPopup").show().delay(1000).fadeOut(500);
            return inputChanges().replaceAll("&lt;", "<").replaceAll("&gt;", ">");
        }
    });
});

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
    if (buttonSize != null) {
        srvModel.buttonSize = buttonSize;
    }

    var html = '&lt;form method="POST" action="' + srvModel.urlRoot + '/stores/'+ srvModel.storeId +'/pay"&gt;';
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
    if (srvModel.buttonSize == 0) {
        width = "146px";
    } else if (srvModel.buttonSize == 1) {
        width = "168px";
    } else if (srvModel.buttonSize == 2) {
        width = "209px";
    }
    html += '\n    &lt;input type="image" src="' + srvModel.payButtonImageUrl + '" name="submit" style="width:' + width +
        '" alt="Pay with BtcPay, Self-Hosted Bitcoin Payment Processor"&gt;';

    html += '\n&lt;/form&gt;';

    $("#mainCode").html(html);

    $('pre code').each(function (i, block) {
        hljs.highlightBlock(block);
    });

    $("#previewButton").css("width", width);
    $("#previewButton").attr("src", srvModel.payButtonImageUrl);

    return html;
}

function addinput(name, value) {
    var html = '\n    &lt;input type="hidden" name="' + name + '" value="' + value + '" /&gt;';
    return html;
}

String.prototype.replaceAll = function (search, replacement) {
    var target = this;
    return target.replace(new RegExp(search, 'g'), replacement);
};
