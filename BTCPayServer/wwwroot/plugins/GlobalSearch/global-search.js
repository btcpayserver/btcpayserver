(function () {
    var vm = window.globalSearch;

    const initGlobalSearch = () => {
        // removeDups remove returns an array with no duplicates.
        // it also merges the keywords of the same item.
        function removeDups(localIndex) {
            var noDups = [];
            var localIndexMap = new Map();
            localIndex.forEach(item => {
                item.keywords ??= [];
                var key = JSON.stringify({category: item.category, title: item.title});
                if (!localIndexMap.has(key)) {
                    localIndexMap.set(key, item)
                    noDups.push(item);
                }
                else
                {
                    var existing = localIndexMap.get(key);
                    item.keywords.forEach(keyword => {existing.keywords.push(keyword)})
                }
            });
            return noDups;
        }


        const nav = document.getElementById('globalNav');
        const shell = document.getElementById('globalSearchShell');
        const mobileToggle = document.getElementById('globalSearchMobileToggle');
        const input = document.getElementById('globalSearchInput');
        const clearButton = document.getElementById('globalSearchClear');
        const backButton = document.getElementById('globalSearchBack');
        const resultsElement = document.getElementById('globalSearchResults');
        if (!shell || !input || !resultsElement) return;

        const localIndexTmp = [];
        vm.items.forEach(item => localIndexTmp.push(item));
        const localIndex = removeDups(localIndexTmp);


        const remoteCache = new Map();
        const maxRemoteCacheEntries = 32;
        const now = new Date();
        const todayIso = now.toISOString().slice(0, 10);
        const yesterday = new Date(now);
        yesterday.setUTCDate(yesterday.getUTCDate() - 1);
        const yesterdayIso = yesterday.toISOString().slice(0, 10);
        const suggestedQueries = [
            {query: `date:${todayIso}`, hint: 'Find invoices and requests created today'},
            {query: `date:${yesterdayIso}`, hint: 'Find invoices and requests from yesterday'},
            {query: 'tx:4e3a67', hint: 'Find by transaction id (prefix supported)'},
            {query: 'server settings', hint: 'Jump to server settings pages quickly'}
        ];

        let latestSearchToken = 0;
        let panelOpen = false;
        let navigationFeedbackTimeout = null;
        const desktopMediaQuery = window.matchMedia('(min-width: 992px)');
        const setBodySearchState = isOpen => {
            if (isOpen) document.body.classList.add('global-search-open');
            else document.body.classList.remove('global-search-open');
        };

        setBodySearchState(false);

        const getCachedRemoteResults = cacheKey => {
            const cached = remoteCache.get(cacheKey);
            if (!Array.isArray(cached)) return null;
            remoteCache.delete(cacheKey);
            remoteCache.set(cacheKey, cached);
            return cached.slice();
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
        const setLoadingState = isLoading => {
            shell.classList.toggle('is-loading', isLoading);
            if (!isLoading && navigationFeedbackTimeout) {
                window.clearTimeout(navigationFeedbackTimeout);
                navigationFeedbackTimeout = null;
            }
        };
        const syncSearchActionState = () => {
            const hasQuery = !!input.value.trim();
            shell.classList.toggle('has-query', hasQuery);
        };

        const showPanel = () => {
            panelOpen = true;
            shell.classList.add('is-open');
            resultsElement.hidden = false;
        };

        const hidePanel = () => {
            panelOpen = false;
            shell.classList.remove('is-open');
            resultsElement.hidden = true;
            setLoadingState(false);
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
            if (input.value.trim()) {
                input.value = '';
                setLoadingState(false);
                syncSearchActionState();
                focusSearch();
                renderInitial();
                return;
            }
            if (isMobileSearchOpen()) {
                closeSearchUi();
                return;
            }
            hidePanel();
        };

        const createResultItem = result => {
            const template = document.getElementById('search-item-template');
            const fragment = template.content.cloneNode(true);

            const listItem = fragment.querySelector('li');
            const button = fragment.querySelector('a');
            if (result.query)
                button.tagName = 'button';
            else
                button.href = result.url;
            const title = fragment.querySelector('.globalSearch-item-title');
            const category = fragment.querySelector('.globalSearch-item-category');
            const subtitle = fragment.querySelector('.globalSearch-item-subtitle');

            if (result.query)
                button.dataset.searchSuggestion = result.query;
            else
                delete button.dataset.searchSuggestion;

            title.firstChild.textContent = result.title;

            if (result.category) {
                category.textContent = result.category;
            } else {
                category.remove();
            }
            if (result.subtitle) {
                subtitle.textContent = result.subtitle;
            } else {
                subtitle.remove();
            }

            if (!result.query) {
                button.addEventListener('click', event => {
                    addGlobalSearchRecent(result);
                    const isModifiedClick = event.metaKey || event.ctrlKey || event.shiftKey || event.altKey;
                    const isPrimaryClick = event.button === 0 && !isModifiedClick;
                    if (!isPrimaryClick || link.target === '_blank') return;
                    hidePanel();
                    setLoadingState(true);
                    navigationFeedbackTimeout = window.setTimeout(() => setLoadingState(false), 2000);
                });
            }
            return listItem;
        };

        const renderGroup = (title, entries, actionLabel = null, actionDataAttribute = null) => {
            if (!entries?.length) return null;

            const template = document.getElementById('globalSearch-group-template');
            const fragment = template.content.cloneNode(true);

            const group = fragment.querySelector('.globalSearch-group');
            const titleEl = fragment.querySelector('.globalSearch-group-title');
            const actionBtn = fragment.querySelector('.globalSearch-group-action');
            const list = fragment.querySelector('.globalSearch-list');

            titleEl.textContent = title;

            // Optional action button
            if (actionLabel && actionDataAttribute) {
                actionBtn.textContent = actionLabel;
                actionBtn.setAttribute(actionDataAttribute, '1');
            } else {
                actionBtn.remove();
            }

            for (const entry of entries) {
                list.appendChild(createResultItem(entry));
            }

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
                    subtitle: local.subtitle || entry.subtitle,
                    category: local.category || entry.category,
                    url: entry.url
                };
            });
        };

        const renderInitial = () => {
            setLoadingState(false);
            syncSearchActionState();
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
            var total = 0;
            return localIndex
                .filter(item => {
                    if (total > 12)
                        return false;
                    const title = (item.title || '');
                    const keywords = (item.keywords || []);
                    var all = [];
                    keywords.forEach(keyword => { all.push(keyword.toLowerCase()); })
                    all.push(title.toLowerCase());
                    return all.some(item => item.startsWith(normalized));
                });
        };

        const searchRemote = async query => {
            if (!query || query.length < 2) return [];
            const cacheKey = `${query}|${vm.storeId || ''}`;
            const cached = getCachedRemoteResults(cacheKey);
            if (cached) return cached;
            const url = new URL(vm.searchUrl, window.location.origin);
            url.searchParams.set('q', query);
            if (vm.storeId) url.searchParams.set('storeId', vm.storeId);
            url.searchParams.set('take', '25');
            const response = await fetch(url.toString(), {credentials: 'include'});
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
            setLoadingState(false);
            syncSearchActionState();
            if (!query) {
                renderInitial();
                return;
            }

            showPanel();
            const token = ++latestSearchToken;
            const localMatches = searchLocal(query);
            renderResults(localMatches);
            // let remoteMatches = [];
            // try {
            //     remoteMatches = await searchRemote(query);
            // } catch {
            //     remoteMatches = [];
            // }
            // if (token !== latestSearchToken || !panelOpen) return;
            // renderResults(mergeResults(remoteMatches, localMatches));
        };

        mobileToggle?.addEventListener('click', () => {
            openMobileSearch();
            renderInitial();
        });
        backButton?.addEventListener('click', closeSearchUi);
        clearButton?.addEventListener('click', clearAndFocus);
        input.addEventListener('focus', () => {
            setLoadingState(false);
            syncSearchActionState();
            if (input.value.trim()) {
                runSearch();
            } else {
                renderInitial();
            }
        });
        input.addEventListener('input', () => {
            syncSearchActionState();
            runSearch();
        });
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

        syncSearchActionState();
    };

    const isEditableElement = element => {
        if (!element) return false;
        const tagName = (element.tagName || '').toLowerCase();
        return tagName === 'input' || tagName === 'textarea' || tagName === 'select' || element.isContentEditable;
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


    document.addEventListener("DOMContentLoaded", () => {
        initGlobalSearch();
    });
})();
