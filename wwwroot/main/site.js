const baseUrl = Object.values(document.scripts).find(s => s.src.includes('/main/site.js')).src.split('/main/site.js').shift();

const flatpickrInstances = [];



const switchTimeFormat = event => {
    const curr = event.target.dataset.mode || 'localized';
    const mode = curr === 'relative' ? 'localized' : 'relative';
    document.querySelectorAll("time[datetime]").forEach($el => {
        $el.innerText = $el.dataset[mode];
    });
    event.target.dataset.mode = mode;
};

async function initLabelManager (elementId) {
    const element = document.getElementById(elementId);

    const labelStyle = data =>
        data && data.color && data.textColor
            ? `--label-bg:${data.color};--label-fg:${data.textColor}`
            : '--label-bg:var(--btcpay-neutral-300);--label-fg:var(--btcpay-neutral-800)'

    if (element) {
        const { fetchUrl, updateUrl, walletId, walletObjectType, walletObjectId, labels, selectElement } = element.dataset;
        const commonCallId = `walletLabels-${walletId}`;
        if (!window[commonCallId]) {
            window[commonCallId] = fetch(fetchUrl, {
                method: 'GET',
                credentials: 'include',
                headers: {
                    'Content-Type': 'application/json'
                },
            }).then(res => res.json());
        }
        const items = element.value.split(',').filter(x => !!x);
        const options = await window[commonCallId].then(labels => {
            const newItems = items.filter(item => !labels.find(label => label.label === item));
            labels = [...labels, ...newItems.map(item => ({ label: item }))];
            return labels;
        });
        const richInfo = labels ? JSON.parse(labels) : {};
        const config = {
            options,
            items,
            valueField: "label",
            labelField: "label",
            searchField: "label",
            create: true,
            persist: true,
            allowEmptyOption: false,
            closeAfterSelect: false,
            render: {
                dropdown (){
                    return '<div class="dropdown-menu"></div>';
                },
                option_create: function(data, escape) {
                    return `<div class="transaction-label create" style="${labelStyle(null)}">Add <strong>${escape(data.input)}</strong>&hellip;</div>`;
                },
                option (data, escape) {
                    return `<div class="transaction-label" style="${labelStyle(data)}"><span>${escape(data.label)}</span></div>`;
                },
                item (data, escape) {
                    const info = richInfo && richInfo[data.label];
                    const additionalInfo = info
                        ? `<a href="${info.link}" target="_blank" rel="noreferrer noopener" class="transaction-label-info transaction-details-icon" title="${info.tooltip}" data-bs-html="true"
                              data-bs-toggle="tooltip" data-bs-custom-class="transaction-label-tooltip"><svg role="img" class="icon icon-info"><use href="/img/icon-sprite.svg#info"></use></svg></a>`
                        : '';
                    const inner = `<span>${escape(data.label)}</span>${additionalInfo}`;
                    return `<div class="transaction-label" style="${labelStyle(data)}">${inner}</div>`;
                }
            },
            onItemAdd (val) {
                window[commonCallId] = window[commonCallId].then(labels => {
                    return [...labels, { label: val }]
                });

                document.dispatchEvent(new CustomEvent(`${commonCallId}-option-added`, {
                    detail: val
                }));
            },
            async onChange (values) {
                const labels = Array.isArray(values) ? values : values.split(',');

                element.dispatchEvent(new CustomEvent("labelmanager:changed", {
                    detail: {
                        walletObjectId,
                        labels: labels
                    }
                }));

                const selectElementI = selectElement ? document.getElementById(selectElement) : null;
                if (selectElementI){
                    while (selectElementI.options.length > 0) {
                        selectElementI.remove(0);
                    }
                    select.items.forEach((item) => {
                        selectElementI.add(new Option(item, item, true, true));
                    })
                }
                if(!updateUrl)
                    return;
                select.lock();
                try {
                    const response = await fetch(updateUrl, {
                        method: "POST",
                        credentials: "include",
                        headers: {
                            'Content-Type': 'application/json'
                        },
                        body: JSON.stringify({
                            id: walletObjectId,
                            type: walletObjectType,
                            labels: select.items
                        })
                    });
                    if (!response.ok) {
                        throw new Error('Network response was not OK');
                    }
                } catch (error) {
                    console.error('There has been a problem with your fetch operation:', error);
                } finally {
                    select.unlock();
                }
            }
        };
        const select = new TomSelect(element, config);

        element.parentElement.querySelectorAll('.ts-control .transaction-label a').forEach(lbl => {
            lbl.addEventListener('click', e => {
                e.stopPropagation()
            })
        })

        document.addEventListener(`${commonCallId}-option-added`, evt => {
            if (!(evt.detail in select.options)) {
                select.addOption({
                    label: evt.detail
                })
            }
        })
    }
}

const initLabelManagers = () => {
    // select only elements which haven't been initialized before, those without data-localized
    document.querySelectorAll("input.label-manager:not(.tomselected)").forEach($el => {
        initLabelManager($el.id)
    });
}

// Remove this hack when browser fix bug https://github.com/btcpayserver/btcpayserver/issues/7003
const reinsertSvgUseElements = () => {
    document.querySelectorAll('svg use').forEach(useElement => {
        const svg = useElement.closest('svg');
        if (svg) {
            const clone = svg.cloneNode(true);
            if (svg.parentNode)
                svg.parentNode.replaceChild(clone, svg);
        }
    });
};

document.addEventListener("DOMContentLoaded", () => {
    reinsertSvgUseElements();
    // sticky header
    const stickyHeader = document.querySelector('#mainContent > section .sticky-header');
    if (stickyHeader) {
        const setStickyHeaderHeight = () => {
            document.documentElement.style.setProperty('--sticky-header-height', `${stickyHeader.offsetHeight}px`)
        }
        window.addEventListener('resize', e => {
            debounce('resize', setStickyHeaderHeight, 50)
        });
        setStickyHeaderHeight();
    }

    // initialize timezone offset value if field is present in page
    const $timezoneOffset = document.getElementById("TimezoneOffset");
    const timezoneOffset = new Date().getTimezoneOffset();
    if ($timezoneOffset) $timezoneOffset.value = timezoneOffset;

    // localize all elements that have localizeDate class
    formatDateTimes();

    initLabelManagers();

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
            codeviewFilterRegex: new RegExp($.summernote.options.codeviewFilterRegex.source + '|<.*?( on\\w+?=.*?)>', 'gi'),
            codeviewIframeWhitelistSrc: ['twitter.com', 'syndication.twitter.com']
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

    delegate('click', '[data-toggle-password]', async e => {
        const $button = e.target.closest('[data-toggle-password]')
        const $el = document.querySelector($button.dataset.togglePassword);
        if (!$el) return;
        const isPassword = $el.getAttribute('type') === 'password';
        if (isPassword) {
            $el.setAttribute('type', 'text')
            if (!!$button.innerHTML.match('#actions-show')) $button.innerHTML = $button.innerHTML.replace('#actions-show', '#actions-hide');
        } else {
            $el.setAttribute('type', 'password')
            if (!!$button.innerHTML.match('#actions-hide')) $button.innerHTML = $button.innerHTML.replace('#actions-hide', '#actions-show');
        }
    })

    // Invoice Status
    delegate('click', '[data-invoice-state-badge] [data-invoice-id][data-new-state]', async e => {
        const $button = e.target
        const $badge = $button.closest('[data-invoice-state-badge]')
        const { invoiceId, newState } = $button.dataset

        $badge.classList.add('pe-none'); // disable further interaction
        const response = await fetch(`${baseUrl}/invoices/${invoiceId}/changestate/${newState}`, { method: 'POST' })
        if (response.ok) {
            const { statusString } = await response.json()
            $badge.outerHTML = `<div class="badge badge-${newState}" data-invoice-state-badge="${invoiceId}">${statusString}</div>`
        } else {
            $badge.classList.remove('pe-none');
            alert("Invoice state update failed");
        }
    })

    // Time Format
    delegate('click', '.switch-time-format', switchTimeFormat);

    // Theme Switch
    delegate('click', '.btcpay-theme-switch [data-theme]', e => {
        e.preventDefault()
        const $btn = e.target.closest('.btcpay-theme-switch [data-theme]')
        setColorMode($btn.dataset.theme)
        $btn.blur()
    })

    // Sensitive Info
    const SENSITIVE_INFO_STORE_KEY = 'btcpay-hide-sensitive-info';
    const SENSITIVE_INFO_DATA_ATTR = 'data-hide-sensitive-info';
    delegate('change', '#HideSensitiveInfo', e => {
        e.preventDefault()
        const isActive = window.localStorage.getItem(SENSITIVE_INFO_STORE_KEY) === 'true';
        if (isActive) {
            window.localStorage.removeItem(SENSITIVE_INFO_STORE_KEY);
            document.documentElement.removeAttribute(SENSITIVE_INFO_DATA_ATTR);
        } else {
            window.localStorage.setItem(SENSITIVE_INFO_STORE_KEY, 'true');
            document.documentElement.setAttribute(SENSITIVE_INFO_DATA_ATTR, 'true');
        }
    });

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

    // Mass Action Tables
    const updateSelectedCount = ($table) => {
        const selectedCount = document.querySelectorAll('.mass-action-select:checked').length;
        const $selectedCount = $table.querySelector('.mass-action-selected-count');
        if ($selectedCount) $selectedCount.innerText = selectedCount;
        if (selectedCount === 0) {
            $table.removeAttribute('data-selected');
        } else {
            $table.setAttribute('data-selected', selectedCount.toString());
        }
    }

    delegate('click', '.mass-action .mass-action-select-all', e => {
        const $table = e.target.closest('.mass-action');
        const { checked } = e.target;
        $table.querySelectorAll('.mass-action-select,.mass-action-select-all').forEach($checkbox => {
            $checkbox.checked = checked;
        });
        updateSelectedCount($table);
    });

    delegate('change', '.mass-action .mass-action-select', e => {
        const $table = e.target.closest('.mass-action');
        const selectedCount = $table.querySelectorAll('.mass-action-select:checked').length;
        if (selectedCount === 0) {
            $table.querySelectorAll('.mass-action-select-all').forEach(checkbox => {
                checkbox.checked = false;
            });
        }
        updateSelectedCount($table);
    });

    delegate('click', '.mass-action .mass-action-row', e => {
        const $target = e.target
        if ($target.matches('td,time,span[data-sensitive]')) {
            const $row = $target.closest('.mass-action-row');
            $row.querySelector('.mass-action-select').click();
        }
    });
});

// Initialize Blazor
if (window.Blazor) {
    let isUnloading = false;
    window.addEventListener("beforeunload", () => { isUnloading = true; });
    let brokenConnection = {
        isConnected: false,
        titleContent: 'Connection broken',
        innerHTML: 'Please <a href="">refresh the page</a>.'
    };
    let interruptedConnection = {
        isConnected: false,
        titleContent: 'Connection interrupted',
        innerHTML: 'Attempt to reestablish the connection in a few seconds...'
    };
    let successfulConnection = {
        isConnected: true,
        titleContent: 'Connection established',
        innerHTML: '' // use empty link on purpose
    };
    class BlazorReconnectionHandler {
        reconnecting = false;
        async onConnectionDown(options, _error) {
            if (this.reconnecting)
                return;
            this.setBlazorStatus(interruptedConnection);
            this.reconnecting = true;
            console.debug('Blazor hub connection lost');
            await this.reconnect();
        }

        async reconnect() {
            let delays = [500, 1000, 2000, 4000, 8000, 16000, 20000, 40000];
            let i = 0;
            const lastDelay = delays.length - 1;
            while (i < delays.length) {
                await this.delay(delays[i]);
                try {
                    if (await Blazor.reconnect())
                        return;
                    console.warn('Error while reconnecting to Blazor hub (Broken circuit)');
                    break;
                }
                catch (err) {
                    this.setBlazorStatus(interruptedConnection);
                    console.warn(`Error while reconnecting to Blazor hub (${err})`);
                }
                i++;
            }
            this.setBlazorStatus(brokenConnection);
        }
        onConnectionUp() {
            this.reconnecting = false;
            console.debug('Blazor hub connected');
            this.setBlazorStatus(successfulConnection);
        }

        setBlazorStatus(content) {
            document.querySelectorAll('.blazor-status').forEach($status => {
                const $state = $status.querySelector('.blazor-status__state');
                const $title = $status.querySelector('.blazor-status__title');
                const $body = $status.querySelector('.blazor-status__body');
                $state.classList.remove('btcpay-status--enabled');
                $state.classList.remove('btcpay-status--disabled');
                $state.classList.add(content.isConnected ? 'btcpay-status--enabled' : 'btcpay-status--disabled');
                $title.textContent = content.titleContent;
                $body.innerHTML = content.innerHTML;
                $body.classList.toggle('d-none', content.isConnected);
                if (!isUnloading) {
                    const toast = new bootstrap.Toast($status, { autohide: false });
                    if (content.isConnected) {
                        if (toast.isShown())
                            toast.hide();
                    }
                    else {
                        if (!toast.isShown())
                            toast.show();
                    }
                }
            });
        }
        delay(durationMilliseconds) {
            return new Promise(resolve => setTimeout(resolve, durationMilliseconds));
        }
    }

    const handler = new BlazorReconnectionHandler();
    handler.setBlazorStatus(successfulConnection);
    Blazor.start({
        reconnectionHandler: handler
    });
}

String.prototype.noExponents= function(){
    const data = String(this).split(/[eE]/);
    if(data.length== 1) return data[0];

    var  z= '', sign= this<0? '-':'',
        str= data[0].replace('.', ''),
        mag= Number(data[1])+ 1;

    if(mag<0){
        z= sign + '0.';
        while(mag++) z += '0';
        return z + str.replace(/^\-/,'');
    }
    mag -= str.length;
    while(mag--) z += '0';
    return str + z;
}

Number.prototype.noExponents= function(){
    return  String(this).noExponents();
};
