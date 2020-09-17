function updateFiatValue(element) {

    if (!element) {
        element = $(this);
    }
    var rateStr = $("#Rate").val();
    var divisibilityStr = $("#FiatDivisibility").val();
    var rate = parseFloat(rateStr);
    var divisibility = parseInt(divisibilityStr);
    if (!isNaN(rate) && !isNaN(divisibility)) {
        var fiatValue = $(element).parents(".input-group").first().find(".fiat-value")
        var fiatValueInput = fiatValue.find(".fiat-value-edit-input");
        var amountValue = parseFloat($(element).val());
        if (!isNaN(amountValue)) {
            fiatValue.show();
            fiatValueInput.val((rate * amountValue).toFixed(divisibility));
        } else {
            fiatValue.hide();
            fiatValueInput.val("")
        }
    }
}

function updateCryptoValue(element) {

    if (!element) {
        element = $(this);
    }

    var divisibilityStr = $("#CryptoDivisibility").val();
    var divisibility = parseInt(divisibilityStr);
    var rateStr = $("#Rate").val();
    var rate = parseFloat(rateStr);
    if (!isNaN(rate)) {
        var cryptoValueInput =  $(element).parents(".input-group").first().find(".output-amount");
        var amountValue = parseFloat($(element).val());
        if (!isNaN(amountValue)) {
            cryptoValueInput.val(( amountValue/rate).toFixed(divisibility));
        } else {
            cryptoValueInput.val("")
        }
    }
}



function updateFiatValueWithCurrentElement() {
    updateFiatValue($(this))
}

function updateCryptoValueWithCurrentElement(){
    updateCryptoValue($(this))
}

function selectCorrectFeeOption(){
    var val =  $("#FeeSatoshiPerByte").val();
    $(".feerate-options").children(".crypto-fee-link").removeClass("active");
    $(".feerate-options").find("[value='"+val+"']").first().addClass("active");
}

$(function () {
    $(".output-amount").on("input", updateFiatValueWithCurrentElement).each(updateFiatValueWithCurrentElement);
    $(".fiat-value-edit-input").on("input", updateCryptoValueWithCurrentElement);

    $(".crypto-fee-link").on("click", function (elem) {
        $(this).parent().children().removeClass("active");
        var val = $(this).addClass("active").val();
        $("#FeeSatoshiPerByte").val(val);
        return false;
    });
    $("#FeeSatoshiPerByte").on("change input", selectCorrectFeeOption);

    selectCorrectFeeOption();
    $(".crypto-balance-link").on("click", function (elem) {
        var val = $(this).text();
        var parentContainer = $(this).parents(".form-group");
        var outputAmountElement = parentContainer.find(".output-amount");
        outputAmountElement.val(val);
        var subtractFeesEl = parentContainer.find(".subtract-fees");
        if(subtractFeesEl.length === 0)
            subtractFeesEl = $(".subtract-fees");
        subtractFeesEl.prop('checked', true);
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
