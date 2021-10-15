delegate('click', '.payment-method', function(e) {
    closePaymentMethodDialog(e.target.dataset.paymentMethod);
    return false;
})
