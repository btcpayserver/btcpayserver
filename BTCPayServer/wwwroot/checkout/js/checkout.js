class NDEFReaderWrapper {
    constructor() {
        this.onreading = null;
        this.onreadingerror = null;
    }

    async scan(opts) {
        if (opts && opts.signal){
            opts.signal.addEventListener('abort', () => {
                window.parent.postMessage('nfc:abort', '*');
            });
        }
        window.parent.postMessage('nfc:startScan', '*');
    }
}

delegate('click', '.payment-method', e => {
    const el = e.target.closest('.payment-method')
    closePaymentMethodDialog(el.dataset.paymentMethod);
    return false;
})
