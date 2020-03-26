function updateFiatValue(element) {

    if (!element) {
        element = $(this);
    }
    var rateStr = $("#Rate").val();
    var divisibilityStr = $("#Divisibility").val();
    var fiat = $("#Fiat").val();
    var rate = parseFloat(rateStr);
    var divisibility = parseInt(divisibilityStr);
    if (!isNaN(rate) && !isNaN(divisibility)) {
        var fiatValue = $(element).parents(".input-group").first().find(".fiat-value");
        var amountValue = parseFloat($(element).val());
        if (!isNaN(amountValue)) {
            fiatValue.show();
            fiatValue.text("= " + (rate * amountValue).toFixed(divisibility) + " " + fiat);
        } else {
            fiatValue.text("");
        }
    }
}

function updateFiatValueWithCurrentElement() {
    updateFiatValue($(this))
}

$(function () {
    $(".output-amount").on("input", updateFiatValueWithCurrentElement).each(updateFiatValueWithCurrentElement);

    $("#crypto-fee-link").on("click", function (elem) {
        var val = $(this).text();
        $("#FeeSatoshiPerByte").val(val);
        return false;
    });

    $(".crypto-balance-link").on("click", function (elem) {
        var val = $(this).text();
        var parentContainer = $(this).parents(".form-group");
        var outputAmountElement = parentContainer.find(".output-amount");
        outputAmountElement.val(val);
        parentContainer.find(".subtract-fees").prop('checked', true);
        updateFiatValue(outputAmountElement);
        return false;
    });

    $("#bip21parse").on("click", function(){
        var bip21 = prompt("Paste BIP21 here");
        if(bip21){
            $("#BIP21").val(bip21);
            $("form").submit();
        }
    });
});
