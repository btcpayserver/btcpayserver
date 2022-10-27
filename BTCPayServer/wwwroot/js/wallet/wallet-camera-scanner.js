$(function () {
    initCameraScanningApp("Scan address/ payment link", data => {
        $("#BIP21").val(data);
        $("form").submit();
    }, "scanModal");
});
