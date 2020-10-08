$(document).ready(function() {

    $(".richtext").summernote({
        minHeight: 300,
        tableClassName: 'table table-sm',
        insertTableMaxSize: {
            col: 5,
            row: 10
        }
    });
});
