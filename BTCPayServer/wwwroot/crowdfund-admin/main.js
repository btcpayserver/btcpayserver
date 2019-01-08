hljs.initHighlightingOnLoad();
$(document).ready(function() {

    $(".richtext").summernote();
    $(".datetime").flatpickr({
        enableTime: true
    });
});
