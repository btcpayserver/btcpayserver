function delegate(eventType, selector, handler, root) {
    (root || document).addEventListener(eventType, function(event) {
        const target = event.target.closest(selector)
        if (target) {
            event.target = target
            if (handler.call(this, event) === false) {
                event.preventDefault()
            }
        }
    })
}

const DEBOUNCE_TIMERS = {}
function debounce(key, fn, delay = 250) {
    clearTimeout(DEBOUNCE_TIMERS[key])
    DEBOUNCE_TIMERS[key] = setTimeout(fn, delay)
}

function formatDateTimes(format) {
    // select only elements which haven't been initialized before, those without data-localized
    document.querySelectorAll("time[datetime]:not([data-localized])").forEach($el => {
        const date = new Date($el.getAttribute("datetime"));
        // https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Intl/DateTimeFormat/DateTimeFormat
        const { dateStyle = 'short', timeStyle = 'short' } = $el.dataset;
        // initialize and set localized attribute
        $el.dataset.localized = new Intl.DateTimeFormat('default', { dateStyle, timeStyle }).format(date);
        // set text to chosen mode
        const mode = format || $el.dataset.initial;
        if ($el.dataset[mode]) $el.innerText = $el.dataset[mode];
    });
}
