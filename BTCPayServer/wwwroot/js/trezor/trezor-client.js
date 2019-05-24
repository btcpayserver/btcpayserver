window.deviceList = null;
window.trezorClient = {
    init: function () {
        document.getElementById("trezor-loading").style.display = "block";
        window.trezorDeviceList = new trezor.DeviceList({
            config: window.trezorConfig || null,
            debug: true,
            transport: new trezorLink.Lowlevel(new trezorLink.WebUsb(), function () {
                return null;
            })
        });
        
        trezorDeviceList.on("connect", trezorClient.onDeviceConnected);
        trezorDeviceList.on("connectUnacquired", function(e){
            e.steal.then(trezorClient.onDeviceConnected);
        });

        trezorDeviceList.on("transport", function(){
            if (trezorDeviceList.asArray().length < 1 || trezorDeviceList.requestNeeded) {
                if (!navigator.usb) {
                    document.getElementById("trezor-loading").style.display = "none";
                    document.getElementById("trezor-error").style.display = "block";
                    document.getElementById("trezor-error").innerHTML = 'Your browser does not support WebUsb. Please switch to a <a href="https://caniuse.com/#feat=webusb" target="_blank">supported browser</a> or request Trezor to implement <a href="https://github.com/trezor/trezord-go/issues/155" target="_blank">this feature</a>.';
                    return;
                }
                trezorClient.requestDevice();
            }
        });

        
    },
    requestDevice: function () {
        return trezorDeviceList.requestDevice().catch(function () {

            document.getElementById("trezor-loading").style.display = "none";
            document.getElementById("trezor-error").style.display = "block";
            document.getElementById("trezor-error").innerText = 'Device could not be acquired. Do you have another app using the device?';
        })
    },
    onDeviceConnected: function (device) {
        window.trezorDevice = null;
        document.getElementById("trezor-error").style.display = "none";
        document.getElementById("trezor-error").innerText = 'Device could not be used.';
        device.on('disconnect', function () {
            window.trezorDevice = null;
            document.getElementById("trezor-error").style.display = "block";
            document.getElementById("trezor-error").innerText = 'Device was disconnected';
            document.getElementById("trezor-loading").style.display = "block";
            document.getElementById("trezor-success").style.display = "none";
            if (window.onTrezorDeviceLost) {
                window.onTrezorDeviceLost();
            }
        });
        if (device.isBootloader()) {
            document.getElementById("trezor-error").style.display = "block";
            document.getElementById("trezor-error").innerText = 'Device is in Bootloader, please reconnect it.';
            return;
        }
        if (!device.isInitialized()) {
            document.getElementById("trezor-error").style.display = "block";
            document.getElementById("trezor-error").innerText = 'Device is not yet setup.';
            return;
        }
        document.getElementById("trezor-loading").style.display = "none";
        document.getElementById("trezor-success").style.display = "block";
        window.trezorDevice = device;
        if (window.onTrezorDeviceFound) {
            document.getElementById("trezor-devicename").innerText = device.features.label;
            window.onTrezorDeviceFound(device);
        }
    }
};

