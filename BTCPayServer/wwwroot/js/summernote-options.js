window.summernoteOptions = function() {
    return {
        minHeight: 300,
        tableClassName: 'table table-sm',
        insertTableMaxSize: {
            col: 5,
            row: 10
        },
        codeviewFilter: true,
        codeviewFilterRegex: new RegExp($.summernote.options.codeviewFilterRegex.source + '|<.*?( on\\w+?=.*?)>', 'gi')
    }
}
