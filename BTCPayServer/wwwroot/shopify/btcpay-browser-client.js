/* Based on @djseeds script: https://github.com/btcpayserver/btcpayserver/issues/36#issuecomment-633109155 */
/*** Creates a new BTCPayServer Modal
 * @param url - BTCPayServer Base URL
 * @param storeId - BTCPayServer store ID
 * @param data - Data to use for invoice creation
* @returns - A promise that resolves when invoice is paid.
* ***/
var BtcPayServerModal = (function () {
    function waitForPayment(btcPayServerUrl, invoiceId, storeId) {
        // Todo: mutex lock on btcpayserver modal.
        return new Promise(function (resolve, reject) {
            // Don't allow two modals at once.
            if (waitForPayment.lock) {
                resolve(null);
            }
            else {
                waitForPayment.lock = true;
            }
            window.btcpay.onModalWillEnter(function () {
                var interval = setInterval(function () {
                    getBtcPayInvoice(btcPayServerUrl, invoiceId, storeId)
                        .then(function (invoice) {
                            if (invoice.status == "complete") {
                                clearInterval(interval);
                                resolve(invoice);
                            }
                        })
                        .catch(function (err) {
                            clearInterval(interval);
                            reject(err);
                        });
                }, 1000);
                window.btcpay.onModalWillLeave(function () {
                    waitForPayment.lock = false;
                    clearInterval(interval);
                    // If user exited the payment modal,
                    // indicate that there was no error but invoice did not complete.
                    resolve(null);
                });
            });
            window.btcpay.showInvoice(invoiceId);
        });
    }

    function getBtcPayInvoice(btcPayServerUrl, invoiceId, storeId) {
        const url = btcPayServerUrl + "/invoices/" + invoiceId + "?storeId=" + storeId;
        return fetch(url, {
            method: "GET",
            mode: "cors", // no-cors, cors, *same-origin,
            headers: {
                "Content-Type": "application/json",
                "accept": "application/json",
            }
        })
            .then(function (response) {
                return response.json();
            })
            .then(function (json) {
                return json.data;
            })
    }

    return {
        show: function (url, storeId, data) {
            const path = url + "/invoices?storeId=" + storeId;
            return fetch(path,
                {
                    method: "POST",
                    mode: "cors",
                    headers: {
                        "Content-Type": "application/json",
                        "accept": "application/json",
                    },
                    body: JSON.stringify(data)
                }
            )
                .then(function (response) {
                    return response.json();
                })
                .then(function (response) {
                    return waitForPayment(url, response.data.id, storeId);
                });
        },
        hide: function () {
            window.btcpay.hideFrame();
        }
    }
})()
