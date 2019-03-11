
var hubListener = function(){
   
    var connection = new signalR.HubConnectionBuilder().withUrl(srvModel.hubPath).build();

    connection.onclose(function(){
        eventAggregator.$emit("connection-lost");
        console.error("Connection was closed. Attempting reconnect in 2s");
        setTimeout(connect, 2000);
    });
    connection.on("PaymentReceived", function(amount, cryptoCode, type){
        eventAggregator.$emit("payment-received", amount,cryptoCode, type);
    });
    connection.on("InvoiceCreated", function(invoiceId){
        eventAggregator.$emit("invoice-created", invoiceId);
    });
    connection.on("InvoiceError", function(error){
        eventAggregator.$emit("invoice-error", error);
    });
    connection.on("InfoUpdated", function(model){
        eventAggregator.$emit("info-updated", model);
    });
    
    function connect(){

        eventAggregator.$emit("connection-pending");
        connection
            .start()
            .then(function(){
                connection.invoke("ListenToCrowdfundApp", srvModel.appId);
                
            })
            .catch(function (err) {
                eventAggregator.$emit("connection-failed");
                console.error("Could not connect to backend. Retrying in 2s", err );
                setTimeout(connect, 2000);
            });
    }


    eventAggregator.$on("contribute", function(model){
        connection.invoke("CreateInvoice", model);
    });
    
    
    return {
        connect: connect
    };
}();

