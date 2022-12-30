const flatpickrInstances = [];

// https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Intl/DateTimeFormat/DateTimeFormat
const dtFormatOpts = { dateStyle: 'short', timeStyle: 'short' };

const formatDateTimes = format => {
    // select only elements which haven't been initialized before, those without data-localized
    document.querySelectorAll("time[datetime]:not([data-localized])").forEach($el => {
        const date = new Date($el.getAttribute("datetime"));
        // initialize and set localized attribute
        $el.dataset.localized = new Intl.DateTimeFormat('default', dtFormatOpts).format(date);
        // set text to chosen mode
        const mode = format || $el.dataset.initial;
        if ($el.dataset[mode]) $el.innerText = $el.dataset[mode];
    });
};

const switchTimeFormat = event => {
    const curr = event.target.dataset.mode || 'localized';
    const mode = curr === 'relative' ? 'localized' : 'relative';
    document.querySelectorAll("time[datetime]").forEach($el => {
        $el.innerText = $el.dataset[mode];
    });
    event.target.dataset.mode = mode;
};

document.addEventListener("DOMContentLoaded", () => {
    // sticky header
    const stickyHeader = document.querySelector('.sticky-header-setup + .sticky-header');
    if (stickyHeader) {
        document.documentElement.style.scrollPaddingTop = `calc(${stickyHeader.offsetHeight}px + var(--btcpay-space-m))`;
    }
    
    // initialize timezone offset value if field is present in page
    var timezoneOffset = new Date().getTimezoneOffset();
    $("#TimezoneOffset").val(timezoneOffset);

    // localize all elements that have localizeDate class
    formatDateTimes();
    
    function updateTimeAgo(){
        var timeagoElements = $("[data-timeago-unixms]");
        timeagoElements.each(function () {
            var elem = $(this);
            elem.text(moment(elem.data("timeago-unixms")).fromNow());
        });
        setTimeout(updateTimeAgo, 1000);
    }
    updateTimeAgo();
    
    // intializing date time pickers
    $(".flatdtpicker").each(function () {
        var element = $(this);
        var fdtp = element.attr("data-fdtp");

        // support for initializing with special options per instance
        if (fdtp) {
            var parsed = JSON.parse(fdtp);
            flatpickrInstances.push(element.flatpickr(parsed));
        } else {
            var min = element.attr("min");
            var max = element.attr("max");
            var defaultDate = element.attr("value");
            flatpickrInstances.push(element.flatpickr({
                enableTime: true,
                enableSeconds: true,
                dateFormat: 'Z',
                altInput: true,
                altFormat: 'Y-m-d H:i:S',
                minDate: min,
                maxDate: max,
                defaultDate: defaultDate,
                time_24hr: true,
                defaultHour: 0,
                static: true
            }));
        }
    });
    // rich text editor
    if ($.summernote) {
        $('.richtext').summernote({
            minHeight: 300,
            tableClassName: 'table table-sm',
            insertTableMaxSize: {
                col: 5,
                row: 10
            },
            codeviewFilter: true,
            codeviewFilterRegex: new RegExp($.summernote.options.codeviewFilterRegex.source + '|<.*?( on\\w+?=.*?)>', 'gi')
        });
    }

    $(".input-group-clear").on("click", function () {
        const input = $(this).parents(".input-group").find("input");
        const event = new CustomEvent('input-group-clear-input-value-cleared', { detail: input });
        input.val(null);
        document.dispatchEvent(event);
        handleInputGroupClearButtonDisplay(this);
    });

    $(".input-group-clear").each(function () {
        var inputGroupClearBtn = this;
        handleInputGroupClearButtonDisplay(inputGroupClearBtn);
        $(this).parents(".input-group").find("input").on("change input", function () {
            handleInputGroupClearButtonDisplay(inputGroupClearBtn);
        });
    });

    $('[data-bs-toggle="tooltip"]').tooltip();

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

    $('[data-toggle="password"]').each(function () {
        var input = $(this);
        var eye_btn = $(this).parent().find('.input-group-text');
        eye_btn.css('cursor', 'pointer').addClass('input-password-hide');
        eye_btn.on('click', function () {
            if (eye_btn.hasClass('input-password-hide')) {
                eye_btn.removeClass('input-password-hide').addClass('input-password-show');
                eye_btn.find('.fa').removeClass('fa-eye').addClass('fa-eye-slash')
                input.attr('type', 'text');
            } else {
                eye_btn.removeClass('input-password-show').addClass('input-password-hide');
                eye_btn.find('.fa').removeClass('fa-eye-slash').addClass('fa-eye')
                input.attr('type', 'password');
            }
        });
    });
    
    // Time Format
    delegate('click', '.switch-time-format', switchTimeFormat);

    // Theme Switch
    delegate('click', '.btcpay-theme-switch', e => {
        e.preventDefault()
        const current = document.documentElement.getAttribute(THEME_ATTR) || COLOR_MODES[0]
        const mode = current === COLOR_MODES[0] ? COLOR_MODES[1] : COLOR_MODES[0]
        setColorMode(mode)
        e.target.closest('.btcpay-theme-switch').blur()
    })
    
    // Currency Selection: Remove the current input value once the element is focused, so that the user gets to
    // see the available options. If no selection or change is made, reset it to the previous value on blur.
    // Note: Use focusin/focusout instead of focus/blur, because the latter do not bubble up and delegate won't work.
    delegate('focusin', 'input[list="currency-selection-suggestion"]', e => {
        e.target.setAttribute('placeholder', e.target.value)
        e.target.value = '';
    })
    delegate('focusout', 'input[list="currency-selection-suggestion"]', e => {
        if (!e.target.value) e.target.value = e.target.getAttribute('placeholder')
        e.target.removeAttribute('placeholder')
    })
    
    // Offcanvas navigation
    const mainMenuToggle = document.getElementById('mainMenuToggle')
    if (mainMenuToggle) {
        delegate('show.bs.offcanvas', '#mainNav', () => {
            mainMenuToggle.setAttribute('aria-expanded', 'true')
        })
        delegate('hide.bs.offcanvas', '#mainNav', () => {
            mainMenuToggle.setAttribute('aria-expanded', 'false')
        })
    }
    
    // Menu collapses
    const mainNav = document.getElementById('mainNav')
    if (mainNav) {
        const COLLAPSED_KEY = 'btcpay-nav-collapsed'
        delegate('show.bs.collapse', '#mainNav', (e) => {
            const { id } = e.target
            const navCollapsed = window.localStorage.getItem(COLLAPSED_KEY)
            const collapsed = navCollapsed ? JSON.parse(navCollapsed).filter(i => i !== id ) : []
            window.localStorage.setItem(COLLAPSED_KEY, JSON.stringify(collapsed))
        })
        delegate('hide.bs.collapse', '#mainNav', (e) => {
            const { id } = e.target
            const navCollapsed = window.localStorage.getItem(COLLAPSED_KEY)
            const collapsed = navCollapsed ? JSON.parse(navCollapsed) : []
            if (!collapsed.includes(id)) collapsed.push(id)
            window.localStorage.setItem(COLLAPSED_KEY, JSON.stringify(collapsed))
        })
    }
});


