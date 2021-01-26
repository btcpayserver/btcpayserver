$(function () {
    initCameraScanningApp("Scan address/ payment link", function(data) {
        $("#BIP21").val(data);
        $("form").submit();
    },"scanModal");
});
