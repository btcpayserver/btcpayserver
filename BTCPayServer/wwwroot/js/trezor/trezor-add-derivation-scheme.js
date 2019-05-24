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

            $("#trezor-error").hide();
            var keypath = $(this).data("trezorkeypath");
            var suffix = $(this).data("derivation-suffix");
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
                        $("#DerivationScheme").val(hdNode.toBase58()+  suffix);
                        $("#trezorsubmitbutton").show();
                    }).catch(function(e){
                        alert(e.message);
                        
                        $("#trezor-error").text("An error occurred when communicating with the trezor device. try with a different USB port?").show();
                        
                })
            }
        });
    $("[data-hide]").on("click", function(){
        $($(this).data("hide")).hide();
    });
    $("[data-show]").on("click", function(){
        $($(this).data("show")).show();
    });
    
    $(".trezor-account-dropdown select").on("input", function(){
        $(this).find(":selected").click();
    });

    $("#trezor-address-type-select").on("input", function(){
        $(this).find(":selected").click();
        $("#RootFingerprint").val("");
        $("#KeyPath").val("");
        $("#DerivationScheme").val("");
        $("#trezorsubmitbutton").hide();
        
    });
    
});

function onTrezorDeviceFound(device) {
    $(".display-when-trezor-connected").show();
}
function onTrezorDeviceLost(){
    $(".display-when-trezor-connected").hide();
    $("#RootFingerprint").val("");
    $(".trezor-account-dropdown").hide();
    $("#KeyPath").val("");
    $("#DerivationScheme").val("");
    $("#trezorsubmitbutton").hide();
}
