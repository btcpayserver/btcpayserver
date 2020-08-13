/* Based on @djseeds script: https://github.com/btcpayserver/btcpayserver/issues/36#issuecomment-633109155 */

/*
 
1. In your BTCPayServer store you need to check "Allow anyone to create invoice"
2. In Shopify Settings > Payment Providers > Manual Payment Methods add one which contains "Bitcoin with BTCPayServer"
3. In Shopify Settings > Checkout > Additional Scripts input the following script, with the details from your BTCPayServer instead of the placeholder values.
<script>
    const BTCPAYSERVER_URL = "FULL_BTCPAYSERVER_URL_WITH_HTTPS";
    const STORE_ID = "YOUR_BTCPAY_STORE_ID";
</script>
<script src="FULL_BTCPAYSERVER_URL_WITH_HTTPS/modal/btcpay.js"></script>
<script src="FULL_BTCPAYSERVER_URL_WITH_HTTPS/shopify/btcpay-browser-client.js"></script>
<script src="FULL_BTCPAYSERVER_URL_WITH_HTTPS/shopify/btcpay-shopify-checkout.js"></script>

 */

! function () {
    // extracted from shopify initialized page
    const shopify_price = Shopify.checkout.payment_due;
    const shopify_currency = Shopify.checkout.currency;

    "use strict";
    const pageElements = document.querySelector.bind(document),
        insertElement = (document.querySelectorAll.bind(document),
            (e,
                n) => {
                n.parentNode.insertBefore(e,
                    n.nextSibling)
            });

    let pageItems = {},
        pageheader = "Thank you!",
        buttonElement = null;

    const setPageItems = () => {
        pageItems = {
            mainHeader: pageElements("#main-header"),
            orderConfirmed: pageElements(".os-step__title"),
            orderConfirmedDescription: pageElements(".os-step__description"),
            continueButton: pageElements(".step__footer__continue-btn"),
            checkMarkIcon: pageElements(".os-header__hanging-icon"),
            orderStatus: pageElements(".os-header__title"),
            paymentMethod: pageElements(".payment-method-list__item__info"),
            price: pageElements(".payment-due__price"),
            finalPrice: pageElements(".total-recap__final-price"),
            orderNumber: pageElements(".os-order-number"),
        }
    }

    const orderPaid = () => {
        pageItems.mainHeader.innerText = pageheader,
            pageItems.orderConfirmed && (pageItems.orderConfirmed.style.display = "block"),
            pageItems.orderConfirmedDescription && (pageItems.orderConfirmedDescription.style.display = "block"),
            pageItems.continueButton && (pageItems.continueButton.style.visibility = "visible"),
            pageItems.checkMarkIcon && (pageItems.checkMarkIcon.style.visibility = "visible"),
            buttonElement && (buttonElement.style.display = "none");
    };

    window.setOrderAsPaid = orderPaid,
        window.openBtcPayShopify = function waitForPaymentMethod() {
            if (setPageItems(), "Order canceled" === pageItems.orderStatus.innerText) {
                return;
            }

            const paymentMethod = pageItems.paymentMethod;

            if (null === paymentMethod) {
                return void setTimeout(() => {
                    waitForPaymentMethod();
                }, 10);
            }

            if (-1 === paymentMethod.innerText.toLowerCase().indexOf("bitcoin")) return;

            // If payment method is bitcoin, display instructions and payment button.
            pageheader = pageItems.mainHeader.innerText,
                pageItems.mainHeader && (pageItems.mainHeader.innerText = "Review and pay!"),
                pageItems.continueButton && (pageItems.continueButton.style.visibility = "hidden"),
                pageItems.checkMarkIcon && (pageItems.checkMarkIcon.style.visibility = "hidden"),
                pageItems.orderConfirmed && (pageItems.orderConfirmed.style.display = "none"),
                pageItems.orderConfirmedDescription && (pageItems.orderConfirmedDescription.style.display = "none");

            const orderId = pageItems.orderNumber.innerText.replace("Order #", "");

            const url = BTCPAYSERVER_URL + "/invoices" + "?storeId=" + STORE_ID + "&orderId=" + orderId + "&status=complete";

            // Check if already paid.
            fetch(url, {
                method: "GET",
                mode: "cors", // no-cors, cors, *same-origin,
                headers: {
                    "Content-Type": "application/json",
                    "accept": "application/json",
                },
            })
                .then(function (response) {
                    return response.json();
                })
                .then(function (json) {
                    return json.data;
                })
                .then(function (data) {
                    if (data.length != 0) {
                        orderPaid();
                    }
                });

            window.waitForPayment = function () {
                buttonElement.innerHTML = "<span>Displaying Invoice...</span>";
                BtcPayServerModal.show(
                    BTCPAYSERVER_URL,
                    STORE_ID,
                    {
                        price: shopify_price,
                        currency: shopify_currency,
                        orderId: orderId
                    }
                )
                    .then(function (invoice) {
                        buttonElement.innerHTML = payButtonHtml;
                        if (invoice != null) {
                            orderPaid();
                        }
                    });
            }

            // Payment button that opens modal
            const payButtonHtml = '<button class="" onclick="window.waitForPayment()" style="width:210px; border: none; outline: none;"><img src="' + BTCPAYSERVER_URL + '/img/paybutton/pay.svg"></button>';

            buttonElement = document.createElement("div");
            buttonElement.innerHTML = payButtonHtml;
            insertElement(buttonElement, pageItems.orderConfirmed);

        }

    window.openBtcPayShopify();
}();
