$(document).ready(function() {
    var trezorInit = false;
    $(".check-for-trezor").on("click",
        function() {
            if (!trezorInit || !window.trezorDevice) {
                trezorClient.init();
                trezorInit = true;
            }
        });
    $("[data-trezorkeypath]").on("click",
        function() {
            var keypath = $(this).data("trezorkeypath");
            var keys = keypath.split("/");
            if (trezorDevice != null) {
                var hardeningConstant = 0x80000000;

                trezorDevice.waitForSessionAndRun(function(session) {
                    var path = [];
                    for (var i = 0; i < keys.length; i++) {
                        var key = keys[i];

                        if (keys[i].endsWith("'")) {
                            key = key.substring(0, key.length - 1);
                            path.push((parseInt(key) | hardeningConstant) >>> 0);
                            continue;
                        }
                        path.push(parseInt(key));
                    }
                    return session.getHDNode(path, window.coinName);
                })
                    .then(function(hdNode) {
                        $("#RootFingerprint").val(hdNode.parentFingerprint);
                        $("#KeyPath").val(keys[keys.length - 1]);
                        $("#DerivationScheme").val(hdNode.toBase58()+  "-[p2sh]");
                        //Seems like Trezor does not allow you to select anything else for their own ui. 
                        // They tell you to use electrum for native segwit: https://wiki.trezor.io/Bech32
                        $("#trezor-submit").submit();
                    });
            }
        });
});

function onTrezorDeviceFound(device) {
    $(".display-when-trezor-connected").show();
}
