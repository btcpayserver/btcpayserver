
var hubListener = function(){

    var statuses = {
        DISCONNECTED: "disconnected",
        CONNECTED: "connected",
        CONNECTING: "connecting"
    };
    var status = "disconnected";
    
    
    var connection = new signalR.HubConnectionBuilder().withUrl("/crowdfundHub").build();

    connection.onclose(function(){
        this.status = statuses.DISCONNECTED;
        console.error("Connection was closed. Attempting reconnect in 2s");
        setTimeout(connect, 2000);
    });

    
    
    function connect(){
        status = statuses.CONNECTING;
        connection
            .start()
            .then(function(){
                this.status = statuses.CONNECTED;
            })
            .catch(function (err) {
                this.status = statuses.DISCONNECTED;
                console.error("Could not connect to backend. Retrying in 2s", err );
                setTimeout(connect, 2000);
            });
    }
    
    return {
        statuses: statuses,
        status: status,
        connect: connect
    };
}();

