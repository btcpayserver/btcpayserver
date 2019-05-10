window.deviceList = null;
var debug = true;

window.trezorClient = {
    
    init: function(){
        window.trezorDeviceList = new trezor.DeviceList({
            config: trezorConfig,
            debug: debug,
            transport: new trezorLink.Lowlevel(new trezorLink.WebUsb(), function () {
                return null;
            })
        });
        trezorDeviceList.on("connect", trezorClient.onDeviceConnected);
        
        if(trezorDeviceList.asArray().length < 1){
            trezorClient.requestDevice();
        }
    },
    requestDevice: function(){
        return trezorDeviceList.requestDevice().then(function(){
            console.warn("device requested");

        });
    },
    onDeviceConnected: function(device){
        window.trezorDevice = null;
        console.log("Connected device " + device.features.label);
        device.on('disconnect', function () {
            window.trezorDevice = null;
            document.getElementById("hw-error").style.display= "block";

            document.getElementById("hw-loading").style.display= "block";
            document.getElementById("hw-success").style.display= "none";
            WriteAlert("danger", 'Device was disconnected');
            
        });
        if (device.isBootloader()) {
            document.getElementById("hw-error").style.display= "block";
            WriteAlert("danger", 'Device is in Bootloader, reconnect it.');
            return;
        }
        // You generally want to filter out devices connected in bootloader mode:
        if (!device.isInitialized()) {
            document.getElementById("hw-error").style.display= "block";
            WriteAlert("danger", 'Device is not yet setup.');
            return;
        }
        document.getElementById("hw-loading").style.display= "none";
        document.getElementById("hw-success").style.display= "block";       
        window.trezorDevice = device;
        if(window.onTrezorDeviceFound){
            window.onTrezorDeviceFound(device);
        }
    }   
    
};



function WriteAlert(type, message) {
    $("#walletAlert").removeClass("alert-danger");
    $("#walletAlert").removeClass("alert-warning");
    $("#walletAlert").removeClass("alert-success");
    $("#walletAlert").addClass("alert-" + type);
    $("#walletAlert").css("display", "block");
    $("#alertMessage").text(message);
}
window.onload = function () {


    window.trezorClient.init();
};
