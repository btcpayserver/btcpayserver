$(function () {
    $(".localizeDate").each(function (index) {
        var serverDate = $(this).text();
        var localDate = new Date(serverDate);

        var dateString = localDate.toLocaleDateString() + " " + localDate.toLocaleTimeString();
        $(this).text(dateString);
    });


    $(".input-group-clear").on("click", function () {
        $(this).parents(".input-group").find("input").val(null);
        handleInputGroupClearButtonDisplay(this);
    });

    $(".input-group-clear").each(function () {
        var inputGroupClearBtn = this;
        handleInputGroupClearButtonDisplay(inputGroupClearBtn);
        $(this).parents(".input-group").find("input").on("change input", function () {
            handleInputGroupClearButtonDisplay(inputGroupClearBtn);
        })
    });


    $(".only-for-js").show();

    function handleInputGroupClearButtonDisplay(element) {
        var value = $(element).parents(".input-group").find("input").val();
        if (!value) {
            $(element).hide();
        } else {
            $(element).show();
        }
    }
});

function switchTimeFormat() {
    $(".switchTimeFormat").each(function (index) {
        var htmlVal = $(this).html();
        var switchVal = $(this).attr("data-switch");

        $(this).html(switchVal);
        $(this).attr("data-switch", htmlVal);
    });
}
