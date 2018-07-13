function bringupDialog() {
    var content = $("#vexPopupDialog").html();
    vex.open({
        unsafeContent: content
    });
}

function closeVexChangeCurrency(currencyId) {
    vex.closeAll();
    return changeCurrency(currencyId);
}
