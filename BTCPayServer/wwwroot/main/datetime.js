// Creates a localized date/time formatter with the given options, merging default formats and locales
// By order of precedence:
// 1. opts
// 2. window.defaultDateTimeFormat (Typically defined in _Layout.cshtml)
function getDateFormater(opts) {
    opts = opts || {};
    const cleanOptions = options => Object.fromEntries(Object.entries(options).filter(([_, value]) => value != null));
    const options = Object.assign(
        cleanOptions(window.defaultDateTimeFormat || {}),
        cleanOptions(opts));
    const locales = opts.locales || (window.defaultDateTimeFormat || {}).locales || 'default';
    // initialize and set localized attribute
    return new Intl.DateTimeFormat(locales, options);
}

function formatDateTimes(format, root) {
    root = root || document;
    // select only elements which haven't been initialized before, those without data-localized
    root.querySelectorAll("time[datetime]:not([data-localized])").forEach($el => {
        const date = new Date($el.getAttribute("datetime"));
        // initialize and set localized attribute
        $el.dataset.localized = getDateFormater($el.dataset).format(date);
        // set text to chosen mode
        const mode = format || $el.dataset.initial;
        if ($el.dataset[mode]) $el.innerText = $el.dataset[mode];
    });
}
