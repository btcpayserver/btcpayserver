window.deviceList = null;
var debug = true;

window.trezorClient = {
    
    init: function(){

        document.getElementById("trezor-loading").style.display= "block";
        window.trezorDeviceList = new trezor.DeviceList({
            config: window.trezorConfig || null,
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
        document.getElementById("trezor-error").style.display= "none";
        document.getElementById("trezor-error").innerText = 'Device could not be used.';
        console.log("Connected device " + device.features.label);
        device.on('disconnect', function () {
            window.trezorDevice = null;
            document.getElementById("trezor-error").style.display= "block";
            document.getElementById("trezor-error").innerText = 'Device was disconnected';
            document.getElementById("trezor-loading").style.display= "block";
            document.getElementById("trezor-success").style.display= "none";
            
        });
        if (device.isBootloader()) {
            document.getElementById("trezor-error").style.display= "block";

            document.getElementById("trezor-error").innerText = 'Device is in Bootloader, reconnect it.';
            return;
        }
        // You generally want to filter out devices connected in bootloader mode:
        if (!device.isInitialized()) {
            document.getElementById("trezor-error").style.display= "block";

            document.getElementById("trezor-error").innerText = 'Device is not yet setup.';
            return;
        }
        document.getElementById("trezor-loading").style.display= "none";
        document.getElementById("trezor-success").style.display= "block";       
        window.trezorDevice = device;
        if(window.onTrezorDeviceFound){
            console.log("Connected device " + device.features.label);
            document.getElementById("trezor-devicename").innerText = device.features.label;
            window.onTrezorDeviceFound(device);
        }
    }   
    
};

