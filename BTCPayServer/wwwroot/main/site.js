$(function () {
    $(".localizeDate").each(function (index) {
        var serverDate = $(this).text();
        var localDate = new Date(serverDate);

        var dateString = localDate.toLocaleDateString() + " " + localDate.toLocaleTimeString();
        $(this).text(dateString);
    });

    $(".flatdtpicker").each(function () {
        var element = $(this);
        var min = element.attr("min");
        var max = element.attr("max");
        var defaultDate = element.attr("value");
        element.flatpickr({
            enableTime: true,
            minDate: min,
            maxDate: max,
            defaultDate: defaultDate,
            dateFormat: 'Z',
            altInput: true,
            altFormat: 'J F Y H:i',
            time_24hr: true,
            parseDate: function (date) {
                return moment(date).toDate();
            }
        });
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
        });
    });


    $(".only-for-js").show();

    function handleInputGroupClearButtonDisplay(element) {
        var inputs = $(element).parents(".input-group").find("input");

        $(element).hide();
        for (var i = 0; i < inputs.length; i++) {
            var el = inputs.get(i);
            if ($(el).val() || el.attributes.value) {
                $(element).show();
                break;
            }
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
