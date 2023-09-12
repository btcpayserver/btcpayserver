var hubListener = function () {

    var connection = new signalR.HubConnectionBuilder().withUrl(srvModel.hubPath).build();

    connection.onclose(function () {
        eventAggregator.$emit("connection-lost");
        console.error("Connection was closed. Attempting reconnect in 2s");
        setTimeout(connect, 2000);
    });
    connection.on("PaymentReceived", function (amount, cryptoCode, type) {
        eventAggregator.$emit("payment-received", amount, cryptoCode, type);
    });
    connection.on("InvoiceCreated", function (invoiceId) {
        eventAggregator.$emit("invoice-created", invoiceId);
    });
    connection.on("InvoiceConfirmed", function (invoiceId) {
        eventAggregator.$emit("invoice-confirmed", invoiceId);
    });
    connection.on("InvoiceError", function (error) {
        eventAggregator.$emit("invoice-error", error);
    });
    connection.on("InfoUpdated", function (model) {
        eventAggregator.$emit("info-updated", model);
    });
    connection.on("InvoiceCancelled", function (model) {
        eventAggregator.$emit("invoice-cancelled", model);
    });
    connection.on("CancelInvoiceError", function (model) {
        eventAggregator.$emit("cancel-invoice-error", model);
    });


    function connect() {

        eventAggregator.$emit("connection-pending");
        connection
            .start()
            .then(function () {
                connection.invoke("ListenToPaymentRequest", srvModel.id);

            })
            .catch(function (err) {
                eventAggregator.$emit("connection-failed");
                console.error("Could not connect to backend. Retrying in 2s", err);
                setTimeout(connect, 2000);
            });
    }

    eventAggregator.$on("pay", function (amount) {
        connection.invoke("Pay", srvModel.id, amount);
    });
    eventAggregator.$on("cancel-invoice", function () {
        connection.invoke("CancelUnpaidPendingInvoice", srvModel.id);
    });


    return {
        connect: connect
    };
}();

