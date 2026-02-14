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
    if (!element) return;

    const {
        fetchUrl,
        updateUrl,
        walletId,
        labels,
        selectElement,
        storeId,
        objectId,
        objectType
    } = element.dataset;

    const isStoreScoped = !!storeId;

    const commonCallId = isStoreScoped
        ? `labels-store-${storeId}-${objectType}`
        : `labels-wallet-${walletId}-${objectType}`;

    const fetchWalletLabels = async (force = false) => {
        if (!fetchUrl) return [];

        if (!force && window[commonCallId])
            return window[commonCallId];

        window[commonCallId] = fetch(fetchUrl, {
            method: 'GET',
            credentials: 'include',
            headers: {
                'Content-Type': 'application/json'
            }
        })
            .then(res => res.json())
            .catch(err => {
            delete window[commonCallId];
            throw err;
        });

        return window[commonCallId];
    };

    const items = element.value.split(',').filter(x => !!x);
    const options = await fetchWalletLabels().then(serverLabels => {
        const newItems = items.filter(item => !serverLabels.find(label => label.label === item));
        return [...serverLabels, ...newItems.map(item => ({ label: item }))];
    });
    const richInfo = labels ? JSON.parse(labels) : {};
    let select;
    const refreshLabelOptions = async () => {
        if (!fetchUrl || !select) return;

        const updatedLabels = await fetchWalletLabels(true);

        updatedLabels.forEach(lbl => {
            if (select.options[lbl.label]) {
                select.updateOption(lbl.label, lbl);
            } else {
                select.addOption(lbl);
            }
        });
    };
    const applyLabelStyle = (el, data) => {
        const bg = data && data.color
            ? data.color
            : 'var(--btcpay-neutral-300)';
        const fg = data && data.textColor
            ? data.textColor
            : 'var(--btcpay-neutral-800)';

        el.style.setProperty('--label-bg', bg);
        el.style.setProperty('--label-fg', fg);
    };

    const config = {
        options,
        items,
        valueField: 'label',
        labelField: 'label',
        searchField: 'label',
        create: true,
        persist: true,
        allowEmptyOption: false,
        closeAfterSelect: false,
        render: {
            dropdown () {
                const menu = document.createElement('div');
                menu.className = 'dropdown-menu';
                return menu;
            },
            option_create (data) {
                const div = document.createElement('div');
                div.className = 'transaction-label create';
                applyLabelStyle(div, null);

                div.append('Add ');
                const strong = document.createElement('strong');
                strong.textContent = data.input;
                div.append(strong, '…');

                return div;
            },
            option (data) {
                const div = document.createElement('div');
                div.className = 'transaction-label';
                applyLabelStyle(div, data);

                const span = document.createElement('span');
                span.textContent = data.label;
                div.append(span);

                return div;
            },
            item (data) {
                const div = document.createElement('div');
                div.className = 'transaction-label';
                applyLabelStyle(div, data);

                const span = document.createElement('span');
                span.textContent = data.label;
                div.append(span);

                const info = richInfo && richInfo[data.label];
                if (info && typeof info.link === 'string') {
                    const url = info.link;
                    if (/^https?:\/\//i.test(url)) {
                        const a = document.createElement('a');
                        a.href = url;
                        a.target = '_blank';
                        a.rel = 'noreferrer noopener';
                        a.className = 'transaction-label-info transaction-details-icon';

                        if (info.tooltip) {
                            a.title = String(info.tooltip);
                        }

                        a.dataset.bsHtml = 'false';
                        a.dataset.bsToggle = 'tooltip';
                        a.dataset.bsCustomClass = 'transaction-label-tooltip';

                        const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
                        svg.setAttribute('role', 'img');
                        svg.classList.add('icon', 'icon-info');

                        const use = document.createElementNS('http://www.w3.org/2000/svg', 'use');
                        use.setAttributeNS('http://www.w3.org/1999/xlink', 'href', `${baseUrl}/img/icon-sprite.svg#info`);
                        svg.appendChild(use);

                        a.appendChild(svg);
                        div.append(a);
                    }
                }

                return div;
            }
        },
        onItemAdd (val) {
            document.dispatchEvent(
                new CustomEvent(`${commonCallId}-option-added`, {
                    detail: val
                }));
        },
        async onChange (values) {
            const labels = Array.isArray(values) ? values : values.split(',');
            element.dispatchEvent(new CustomEvent("labelmanager:changed", {
                detail: {
                    id: objectId,
                    type: objectType,
                    labels
                }
            }));

            const selectElementI = selectElement ? document.getElementById(selectElement) : null;
            if (selectElementI) {
                while (selectElementI.options.length > 0) {
                    selectElementI.remove(0);
                }
                select.items.forEach(item => {
                    selectElementI.add(new Option(item, item, true, true));
                });
            }
            if (!updateUrl) return;
            select.lock();
            try {
                const payload = { id: objectId, type: objectType, labels: select.items };
                const tokenInput =
                    element.closest('form')?.querySelector('input[name="__RequestVerificationToken"]') ||
                    document.querySelector('input[name="__RequestVerificationToken"]');
                const headers = { 'Content-Type': 'application/json' };
                if (tokenInput?.value) headers['RequestVerificationToken'] = tokenInput.value;
                const response = await fetch(updateUrl, {
                    method: 'POST',
                    headers,
                    body: JSON.stringify(payload)
                });
                if (!response.ok) {
                    throw new Error('Network response was not OK');
                }

                await refreshLabelOptions();
            } catch (error) {
                console.error('There has been a problem with your fetch operation:', error);
            } finally {
                select.unlock();
            }
        }
    };
    select = new TomSelect(element, config);

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

const isEditableElement = element => {
    if (!element) return false;
    const tagName = (element.tagName || '').toLowerCase();
    return tagName === 'input' || tagName === 'textarea' || tagName === 'select' || element.isContentEditable;
};

const collectGlobalSearchLinks = () => {
    const candidates = [];
    const selectors = [
        '#mainNav a[href]',
        '#mainNavSettings a[href]',
        '#globalNavPluginsMenu a[href]',
        '#globalNavServerMenu a[href]',
        '#globalNavAccountMenu a[href]'
    ];
    document.querySelectorAll(selectors.join(',')).forEach(anchor => {
        const href = anchor.getAttribute('href');
        const text = anchor.textContent ? anchor.textContent.replace(/\s+/g, ' ').trim() : '';
        if (!href || !text) return;
        if (href.startsWith('#') || href.includes('/logout')) return;
        let absoluteHref;
        try {
            absoluteHref = new URL(href, window.location.href);
        } catch {
            return;
        }
        if (absoluteHref.protocol === 'javascript:') return;
        const isHttpLike = absoluteHref.protocol === 'http:' || absoluteHref.protocol === 'https:';
        const isSameOriginHttp = isHttpLike && absoluteHref.origin === window.location.origin;
        candidates.push({
            title: text,
            subtitle: '',
            category: 'Page',
            url: isSameOriginHttp
                ? `${absoluteHref.pathname}${absoluteHref.search}${absoluteHref.hash}`
                : absoluteHref.toString(),
            keywords: `${text} ${absoluteHref.pathname} ${absoluteHref.hostname}`
        });
    });
    return candidates.filter((item, index, all) =>
        all.findIndex(other => other.url === item.url && other.title === item.title) === index);
};

const GLOBAL_SEARCH_RECENTS_KEY = 'btcpay-global-search-recents';

const loadGlobalSearchRecents = () => {
    try {
        const parsed = JSON.parse(window.localStorage.getItem(GLOBAL_SEARCH_RECENTS_KEY) || '[]');
        return Array.isArray(parsed) ? parsed.slice(0, 8) : [];
    } catch (e) {
        return [];
    }
};

const saveGlobalSearchRecents = items => {
    window.localStorage.setItem(GLOBAL_SEARCH_RECENTS_KEY, JSON.stringify(items.slice(0, 8)));
};

const clearGlobalSearchRecents = () => {
    try {
        window.localStorage.removeItem(GLOBAL_SEARCH_RECENTS_KEY);
    } catch (e) {
        saveGlobalSearchRecents([]);
    }
};

const addGlobalSearchRecent = result => {
    if (!result || !result.url) return;
    const current = loadGlobalSearchRecents().filter(item => item.url !== result.url);
    current.unshift({
        title: result.title,
        subtitle: result.subtitle || '',
        category: result.category || 'Page',
        url: result.url
    });
    saveGlobalSearchRecents(current);
};

const initGlobalSearch = () => {
    const nav = document.getElementById('globalNav');
    const shell = document.getElementById('globalSearchShell');
    const mobileToggle = document.getElementById('globalSearchMobileToggle');
    const input = document.getElementById('globalSearchInput');
    const clearButton = document.getElementById('globalSearchClear');
    const backButton = document.getElementById('globalSearchBack');
    const resultsElement = document.getElementById('globalSearchResults');
    if (!nav || !shell || !input || !resultsElement) return;

    const { searchUrl, storeId } = nav.dataset;
    const localIndex = collectGlobalSearchLinks();
    const remoteCache = new Map();
    const maxRemoteCacheEntries = 32;
    const now = new Date();
    const todayIso = now.toISOString().slice(0, 10);
    const yesterday = new Date(now);
    yesterday.setUTCDate(yesterday.getUTCDate() - 1);
    const yesterdayIso = yesterday.toISOString().slice(0, 10);
    const suggestedQueries = [
        { query: `date:${todayIso}`, hint: 'Find invoices and requests created today' },
        { query: `date:${yesterdayIso}`, hint: 'Find invoices and requests from yesterday' },
        { query: 'tx:4e3a67', hint: 'Find by transaction id (prefix supported)' },
        { query: 'server settings', hint: 'Jump to server settings pages quickly' }
    ];

    let latestSearchToken = 0;
    let panelOpen = false;
    const desktopMediaQuery = window.matchMedia('(min-width: 992px)');
    const setBodySearchState = isOpen => {
        if (isOpen) document.body.classList.add('global-search-open');
        else document.body.classList.remove('global-search-open');
    };

    setBodySearchState(false);

    const getCachedRemoteResults = cacheKey => {
        const cached = remoteCache.get(cacheKey);
        return Array.isArray(cached) ? cached.slice() : null;
    };
    const setCachedRemoteResults = (cacheKey, value) => {
        const cachedValue = Array.isArray(value) ? value.slice() : [];
        if (remoteCache.has(cacheKey)) remoteCache.delete(cacheKey);
        remoteCache.set(cacheKey, cachedValue);
        if (remoteCache.size > maxRemoteCacheEntries) {
            const oldestKey = remoteCache.keys().next().value;
            if (oldestKey !== undefined) remoteCache.delete(oldestKey);
        }
    };

    const isMobileSearchOpen = () => nav.classList.contains('globalSearch-mobile-open');

    const showPanel = () => {
        panelOpen = true;
        shell.classList.add('is-open');
        resultsElement.hidden = false;
    };

    const hidePanel = () => {
        panelOpen = false;
        shell.classList.remove('is-open');
        resultsElement.hidden = true;
    };

    const openMobileSearch = () => {
        if (desktopMediaQuery.matches) return;
        const mainNav = document.getElementById('mainNav');
        if (mainNav && mainNav.classList.contains('show') && window.bootstrap?.Offcanvas) {
            window.bootstrap.Offcanvas.getOrCreateInstance(mainNav).hide();
        }
        nav.classList.add('globalSearch-mobile-open');
        setBodySearchState(true);
        input.focus();
        input.select();
    };

    const closeMobileSearch = () => {
        nav.classList.remove('globalSearch-mobile-open');
        setBodySearchState(false);
    };

    const focusSearch = () => {
        if (!desktopMediaQuery.matches && !isMobileSearchOpen()) {
            openMobileSearch();
        }
        input.focus();
        input.select();
    };

    const closeSearchUi = () => {
        hidePanel();
        if (!desktopMediaQuery.matches) {
            closeMobileSearch();
        }
    };

    const normalizeResult = result => {
        if (!result || !result.url || !result.title) return null;
        let resolvedUrl;
        try {
            resolvedUrl = new URL(result.url, window.location.href);
        } catch {
            return null;
        }
        const protocol = (resolvedUrl.protocol || '').toLowerCase();
        const isHttpLike = protocol === 'http:' || protocol === 'https:';
        if (!isHttpLike && protocol !== 'mailto:' && protocol !== 'tel:') return null;
        const normalizedUrl = isHttpLike && resolvedUrl.origin === window.location.origin
            ? `${resolvedUrl.pathname}${resolvedUrl.search}${resolvedUrl.hash}`
            : resolvedUrl.toString();
        return {
            title: result.title,
            subtitle: result.subtitle || '',
            category: result.category || 'Result',
            url: normalizedUrl
        };
    };

    const toSuggestionResult = item => ({
        title: item.query,
        subtitle: item.hint,
        category: 'Try searching',
        query: item.query
    });

    const clearAndFocus = () => {
        input.value = '';
        focusSearch();
        renderInitial();
    };

    const createResultItem = result => {
        const listItem = document.createElement('li');
        const link = document.createElement('a');
        link.className = 'globalSearch-item';
        if (result.query) {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'globalSearch-item globalSearch-item-button';
            button.dataset.searchSuggestion = result.query;
            const title = document.createElement('span');
            title.className = 'globalSearch-item-title';
            title.textContent = result.title;
            if (result.category) {
                const category = document.createElement('span');
                category.className = 'globalSearch-item-category';
                category.textContent = result.category;
                title.appendChild(category);
            }
            button.appendChild(title);
            if (result.subtitle) {
                const subtitle = document.createElement('div');
                subtitle.className = 'globalSearch-item-subtitle';
                subtitle.textContent = result.subtitle;
                button.appendChild(subtitle);
            }
            listItem.appendChild(button);
            return listItem;
        }

        link.href = result.url;
        link.dataset.searchResult = '1';
        const title = document.createElement('span');
        title.className = 'globalSearch-item-title';
        title.textContent = result.title;
        if (result.category) {
            const category = document.createElement('span');
            category.className = 'globalSearch-item-category';
            category.textContent = result.category;
            title.appendChild(category);
        }
        link.appendChild(title);
        if (result.subtitle) {
            const subtitle = document.createElement('div');
            subtitle.className = 'globalSearch-item-subtitle';
            subtitle.textContent = result.subtitle;
            link.appendChild(subtitle);
        }
        link.addEventListener('click', () => {
            addGlobalSearchRecent(result);
            closeSearchUi();
        });
        listItem.appendChild(link);
        return listItem;
    };

    const renderGroup = (title, entries, actionLabel = null, actionDataAttribute = null) => {
        if (!entries.length) return null;
        const group = document.createElement('div');
        group.className = 'globalSearch-group';
        const headingRow = document.createElement('div');
        headingRow.className = 'globalSearch-group-header';
        const heading = document.createElement('span');
        heading.className = 'globalSearch-group-title';
        heading.textContent = title;
        headingRow.appendChild(heading);
        if (actionLabel && actionDataAttribute) {
            const action = document.createElement('button');
            action.type = 'button';
            action.className = 'globalSearch-group-action';
            action.textContent = actionLabel;
            action.setAttribute(actionDataAttribute, '1');
            headingRow.appendChild(action);
        }
        const list = document.createElement('ul');
        list.className = 'globalSearch-list';
        entries.forEach(entry => {
            list.appendChild(createResultItem(entry));
        });
        group.append(headingRow, list);
        return group;
    };

    const renderEmpty = text => {
        const empty = document.createElement('div');
        empty.className = 'globalSearch-empty';
        empty.textContent = text;
        resultsElement.innerHTML = '';
        resultsElement.appendChild(empty);
    };

    const refreshRecentMetadata = entries => {
        const localByUrl = new Map(localIndex.map(item => [item.url, item]));
        return entries.map(entry => {
            const local = localByUrl.get(entry.url);
            if (!local) return entry;
            return {
                title: local.title || entry.title,
                subtitle: local.subtitle || entry.subtitle || '',
                category: local.category || entry.category,
                url: entry.url
            };
        });
    };

    const renderInitial = () => {
        showPanel();
        resultsElement.innerHTML = '';
        const recent = refreshRecentMetadata(loadGlobalSearchRecents())
            .map(normalizeResult)
            .filter(Boolean);
        const recentGroup = renderGroup('Recent', recent, 'Clear history', 'data-clear-search-history');
        if (recentGroup) resultsElement.appendChild(recentGroup);
        const suggestions = suggestedQueries.map(toSuggestionResult);
        const suggestedGroup = renderGroup('Suggested', suggestions);
        if (suggestedGroup) {
            resultsElement.appendChild(suggestedGroup);
        }

        if (!recentGroup && !suggestedGroup) {
            renderEmpty('Type to search');
        }
    };

    const searchLocal = query => {
        if (!query) return [];
        const normalized = query.toLowerCase();
        return localIndex
            .filter(item => {
                const title = (item.title || '').toLowerCase();
                const keywords = (item.keywords || '').toLowerCase();
                return title.includes(normalized) || keywords.includes(normalized);
            })
            .slice(0, 12);
    };

    const searchRemote = async query => {
        if (!searchUrl || !query || query.length < 2) return [];
        const cacheKey = `${query}|${storeId || ''}`;
        const cached = getCachedRemoteResults(cacheKey);
        if (cached) return cached;
        const url = new URL(searchUrl, window.location.origin);
        url.searchParams.set('q', query);
        if (storeId) url.searchParams.set('storeId', storeId);
        url.searchParams.set('take', '25');
        const response = await fetch(url.toString(), { credentials: 'include' });
        if (!response.ok) return [];
        const payload = await response.json();
        const normalized = (Array.isArray(payload) ? payload : [])
            .map(normalizeResult)
            .filter(Boolean);
        setCachedRemoteResults(cacheKey, normalized);
        return normalized;
    };

    const mergeResults = (remote, local) => {
        const merged = [];
        const seen = {};
        [...remote, ...local].forEach(result => {
            if (!result || !result.url || !result.title) return;
            const key = `${result.url}|${result.title}`.toLowerCase();
            if (seen[key]) return;
            seen[key] = true;
            merged.push(result);
        });
        return merged.slice(0, 25);
    };

    const renderResults = entries => {
        resultsElement.innerHTML = '';
        if (!entries.length) {
            renderEmpty('No matches found');
            return;
        }

        const grouped = entries.reduce((acc, item) => {
            const key = item.category || 'Results';
            if (!acc[key]) acc[key] = [];
            acc[key].push(item);
            return acc;
        }, {});

        Object.keys(grouped).forEach(groupName => {
            const group = renderGroup(groupName, grouped[groupName]);
            if (group) resultsElement.appendChild(group);
        });
    };

    const runSearch = async () => {
        const query = input.value.trim();
        if (!query) {
            renderInitial();
            return;
        }

        showPanel();
        const token = ++latestSearchToken;
        const localMatches = searchLocal(query);
        renderResults(localMatches);
        let remoteMatches = [];
        try {
            remoteMatches = await searchRemote(query);
        } catch {
            remoteMatches = [];
        }
        if (token !== latestSearchToken || !panelOpen) return;
        renderResults(mergeResults(remoteMatches, localMatches));
    };

    mobileToggle?.addEventListener('click', () => {
        openMobileSearch();
        renderInitial();
    });
    backButton?.addEventListener('click', closeSearchUi);
    clearButton?.addEventListener('click', clearAndFocus);
    input.addEventListener('focus', () => {
        if (input.value.trim()) {
            runSearch();
        } else {
            renderInitial();
        }
    });
    input.addEventListener('input', () => debounce('global-search-input', runSearch, 120));
    resultsElement.addEventListener('click', e => {
        const clearHistory = e.target.closest('[data-clear-search-history]');
        if (clearHistory) {
            e.preventDefault();
            clearGlobalSearchRecents();
            renderInitial();
            return;
        }
        const suggestion = e.target.closest('[data-search-suggestion]');
        if (!suggestion) return;
        e.preventDefault();
        input.value = suggestion.dataset.searchSuggestion || '';
        input.focus();
        runSearch();
    });

    document.addEventListener('keydown', e => {
        const openShortcut = (e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k';
        const slashShortcut = e.key === '/' && !isEditableElement(e.target);
        if (openShortcut || slashShortcut) {
            e.preventDefault();
            focusSearch();
            if (input.value.trim()) {
                runSearch();
            } else {
                renderInitial();
            }
            return;
        }

        if (e.key === 'Escape' && (panelOpen || isMobileSearchOpen())) {
            e.preventDefault();
            closeSearchUi();
        }
    });

    document.addEventListener('click', e => {
        const target = e.target;
        if (target instanceof Element && (shell.contains(target) || mobileToggle?.contains(target))) {
            return;
        }
        if (panelOpen || isMobileSearchOpen()) {
            closeSearchUi();
        }
    });

    desktopMediaQuery.addEventListener('change', e => {
        if (e.matches) {
            closeMobileSearch();
            hidePanel();
        }
    });
};

document.addEventListener("DOMContentLoaded", () => {
    reinsertSvgUseElements();
    initGlobalSearch();
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

    const mainNav = document.getElementById('mainNav')
    const closeMobileNav = () => {
        if (!mainNav || !window.matchMedia('(max-width: 991px)').matches || !mainNav.classList.contains('show')) return;
        if (window.bootstrap?.Offcanvas) {
            window.bootstrap.Offcanvas.getOrCreateInstance(mainNav).hide();
        }
    }

    if (mainNav) {
        delegate('click', '#mainNav a[href]', closeMobileNav)

        let startX = 0;
        let startY = 0;
        let trackingSwipe = false;

        mainNav.addEventListener('touchstart', e => {
            if (!mainNav.classList.contains('show') || !e.touches[0]) return;
            startX = e.touches[0].clientX;
            startY = e.touches[0].clientY;
            trackingSwipe = true;
        }, { passive: true });

        mainNav.addEventListener('touchmove', e => {
            if (!trackingSwipe || !e.touches[0]) return;
            const currentX = e.touches[0].clientX;
            const currentY = e.touches[0].clientY;
            const deltaX = currentX - startX;
            const deltaY = currentY - startY;
            if (Math.abs(deltaX) < 64 || Math.abs(deltaX) < Math.abs(deltaY)) return;
            if (deltaX < 0) closeMobileNav();
            trackingSwipe = false;
        }, { passive: true });

        mainNav.addEventListener('touchend', () => {
            trackingSwipe = false;
        }, { passive: true });
    }

    // Menu collapses
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
