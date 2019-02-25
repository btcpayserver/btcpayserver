hljs.initHighlightingOnLoad();
$(document).ready(function() {

    $(".richtext").summernote({
        minHeight: 300
    });
    $(".datetime").each(function(){
        var element = $(this);
        var min = element.attr("min");
        var max = element.attr("max");
        var defaultDate = element.attr("value");
        element.flatpickr({
            enableTime: true,
            minDate: min,
            maxDate: max,
            defaultDate: defaultDate
        });
    });
});
