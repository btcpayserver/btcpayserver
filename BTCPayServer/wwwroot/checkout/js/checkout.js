delegate('click', '.payment-method', e => {
    const el = e.target.closest('.payment-method')
    closePaymentMethodDialog(el.dataset.paymentMethod);
    return false;
})