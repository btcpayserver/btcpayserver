/* Based on @djseeds script: https://github.com/btcpayserver/btcpayserver/issues/36#issuecomment-633109155 */

/*
 
1. In your BTCPayServer store you need to check "Allow anyone to create invoice"
2. In Shopify Settings > Payment Providers > Manual Payment Methods add one which contains "Bitcoin"
3. In Shopify Settings > Checkout > Additional Scripts input the following script, with the details from your BTCPayServer instead of the placeholder values.
<script>
    const BTCPAYSERVER_URL = "https://your-btcpay-server-url:port";
    const STORE_ID = "your-btcpayserver-store-id";
    const DEFAULT_CURRENCY_SYMBOL = "$";
    const DEFAULT_CURRENCY = "USD";
    const THOUSAND_DELIMITER = ",";
</script>
<script src="https://your-btcpay-server-url:port/modal/btcpay.js"></script>
<script src="https://your-btcpay-server-url:port/shopify/btcpay-browser-client.js"></script>
<script src="https://your-btcpay-server-url:port/shopify/btcpay-shopify-checkout.js"></script>

 */

! function () {
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
                pageItems.orderConfirmedDescription && (pageItems.orderConfirmedDescription.style.display = "none"),
                buttonElement = document.createElement("div");

            const priceElement = pageItems.finalPrice || pageItems.price;
            var price = priceElement.innerText.replace(DEFAULT_CURRENCY_SYMBOL, "").replace(THOUSAND_DELIMITER, "");
            if (THOUSAND_DELIMITER === ".") {
                price = price.replace(",", "."); // 5.000,00 needs to become 5000.00
            }
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
                BtcPayServerModal.show(
                    BTCPAYSERVER_URL,
                    STORE_ID,
                    {
                        price: price,
                        currency: DEFAULT_CURRENCY,
                        orderId: orderId
                    }
                )
                    .then(function (invoice) {
                        if (invoice != null) {
                            orderPaid();
                        }
                    });
            }

            // Payment button that opens modal
            buttonElement.innerHTML = `\n    <button class="btn btn-primary" onclick="window.waitForPayment()"/>Pay with BTCPayServer</button>\n`,
                insertElement(buttonElement, pageItems.orderConfirmed);

        }

    window.openBtcPayShopify();
}();
