function updateFiatValue() {
    var rateStr = $("#Rate").val();
    var divisibilityStr = $("#Divisibility").val();
    var fiat = $("#Fiat").val();
    var rate = parseFloat(rateStr);
    var divisibility = parseInt(divisibilityStr);
    if (!isNaN(rate) && !isNaN(divisibility)) {
        var fiatValue = $("#fiatValue");
        var amountValue = parseFloat($("#Amount").val());
        if (!isNaN(amountValue)) {
            fiatValue.css("display", "inline");
            fiatValue.text("= " + (rate * amountValue).toFixed(divisibility) + " " + fiat);
        }
    }
}
$(function () {
    updateFiatValue();
    $("#crypto-fee-link").on("click", function (elem) {
        elem.preventDefault();
        var val = $("#crypto-fee-link").text();
        $("#FeeSatoshiPerByte").val(val);
        return false;
    });

    $("#crypto-balance-link").on("click", function (elem) {
        elem.preventDefault();
        var val = $("#crypto-balance-link").text();
        $("#Amount").val(val);
        $("#SubstractFees").prop('checked', true);
        return false;
    });
});
