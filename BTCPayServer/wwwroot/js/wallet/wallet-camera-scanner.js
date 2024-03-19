window.addEventListener("load", () => {
    let $input = null;
    initCameraScanningApp("Scan address or payment link", data => {
        if (data.includes('?') || $input == null) {
            document.getElementById("BIP21").value = data;
            document.getElementById("SendForm").submit();
        } else {
            $input.value = data;
        }
    }, "scanModal");
    document.getElementById('scanModal').addEventListener('show.bs.modal', e => {
        const { index } = e.relatedTarget.dataset;
        $input = index ? document.querySelector(`[name="Outputs[${index}].DestinationAddress"]`) : null;
    });
});
