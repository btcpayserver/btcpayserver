$(function () {
    $(".localizeDate").each(function (index) {
        var serverDate = $(this).text();
        var localDate = new Date(serverDate);

        var dateString = localDate.toLocaleDateString() + " " + localDate.toLocaleTimeString();
        $(this).text(dateString);
    });

    // intializing date time pickers throughts website
    $(".flatdtpicker").each(function () {
        var element = $(this);
        var fdtp = element.attr("data-fdtp");

        // support for initializing with special options per instance
        if (fdtp) {
            var parsed = JSON.parse(fdtp);
            element.flatpickr(parsed);
        } else {
            var min = element.attr("min");
            var max = element.attr("max");
            var defaultDate = element.attr("value");
            element.flatpickr({
                enableTime: true,
                enableSeconds: true,
                dateFormat: 'Z',
                altInput: true,
                altFormat: 'Y-m-d H:i:S',
                minDate: min,
                maxDate: max,
                defaultDate: defaultDate,
                time_24hr: true,
                defaultHour: 0
            });
        }
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
