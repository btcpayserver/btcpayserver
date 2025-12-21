window.addEventListener("load", () => {
    let outputIndex = null;
    initCameraScanningApp("Scan address or payment link", data => {
        const bip21 = data.match(/bitcoin:(\w+)(?:\?(.*))?/i)
        if (bip21 && outputIndex == null) {
            document.getElementById("BIP21").value = data;
            document.getElementById("SendForm").submit();
        } else if (bip21 && bip21.length >= 2) {
            document.querySelector(`[name="Outputs[${outputIndex}].DestinationAddress"]`).value = bip21[1];
            if (bip21.length === 3) {
                const p = new URLSearchParams(bip21[2]);
                if (p.has('amount')) {
                    document.querySelector(`[name="Outputs[${outputIndex}].Amount"]`).value = p.get('amount');
                }
            }
        } else {
            document.querySelector(`[name="Outputs[${outputIndex}].DestinationAddress"]`).value = data;
        }
    }, 'scanModal');
    const $scanModal = document.getElementById('scanModal');
    $scanModal.addEventListener('show.bs.modal', e => {
        const { index } = e.relatedTarget.dataset;
        outputIndex = index ? index : null;
    });
    $scanModal.addEventListener('hide.bs.modal', e => {
        outputIndex = null;
    });
});
