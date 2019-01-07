hljs.initHighlightingOnLoad();
$(document).ready(function() {
    $(".richtext").richText();
    $(".datetime").flatpickr({
        enableTime: true
    });
});
