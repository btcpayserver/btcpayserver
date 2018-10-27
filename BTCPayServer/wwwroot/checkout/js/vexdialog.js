function openPaymentMethodDialog() {
    var content = $("#vexPopupDialog").html();
    vex.open({
        unsafeContent: content
    });
}

function closePaymentMethodDialog(currencyId) {
    vex.closeAll();
    return changeCurrency(currencyId);
}
