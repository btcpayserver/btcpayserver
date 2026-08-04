// Wallet transactions table column customization.
// Lets users show/hide and reorder the table's content columns, persisting the
// choice in localStorage. Column-agnostic: the available columns are derived
// from the header cells tagged with `data-col` / `data-col-label`, so the
// control adapts automatically as columns are added or removed from the view.
(function () {
    const STORAGE_KEY_BASE = 'btcpay-wallet-tx-columns';
    const TABLE_ID = 'WalletTransactions';
    const BODY_ID = 'WalletTransactionsList';
    const LIST_ID = 'ColumnsList';
    const RESET_ID = 'ColumnsReset';
    const DRAGGING_CLASS = 'wallet-columns__item--dragging';
    const SVG_NS = 'http://www.w3.org/2000/svg';

    let state = { registry: [], order: [], hidden: [] };
    let storeId = '';
    let moveLabelFormat = 'Move {0}'; // localized template supplied by the view

    const getTable = () => document.getElementById(TABLE_ID);
    const getHeaderRow = () => {
        const table = getTable();
        return table ? table.querySelector('thead.mass-action-head tr') : null;
    };

    // Available columns, in header (DOM) order, deduped by key (e.g. multiple rate columns).
    // Cached on first read so it reflects the original layout, since apply() reorders the header DOM
    // and a later Reset must restore that original order rather than the reordered one.
    let registryCache = null;
    const getRegistry = () => {
        if (registryCache) return registryCache;
        const header = getHeaderRow();
        if (!header) return [];
        const seen = new Set();
        const cols = [];
        header.querySelectorAll(':scope > [data-col]').forEach(th => {
            const key = th.dataset.col;
            if (!key || seen.has(key)) return;
            seen.add(key);
            cols.push({ key, label: th.dataset.colLabel || key });
        });
        registryCache = cols;
        return cols;
    };

    // Scope preferences to the store and current column set ("schema").
    const storageKey = () => `${STORAGE_KEY_BASE}:${storeId}`;

    const loadSaved = () => {
        try {
            const parsed = JSON.parse(window.localStorage.getItem(storageKey()));
            if (!parsed || !Array.isArray(parsed.order)) return null;
            return { order: parsed.order, hidden: Array.isArray(parsed.hidden) ? parsed.hidden : [] };
        } catch (e) {
            return null;
        }
    };

    const persist = () => {
        try {
            window.localStorage.setItem(storageKey(), JSON.stringify({ order: state.order, hidden: state.hidden }));
        } catch (e) { /* storage unavailable — degrade gracefully */ }
    };

    // Reconcile any saved preference with the columns actually present.
    const resolveState = () => {
        const registry = getRegistry();
        const keys = registry.map(c => c.key);
        const saved = loadSaved();
        if (!saved) return { registry, order: [...keys], hidden: [] };
        // Keep only known keys, de-duplicated, then append any columns missing from the saved order.
        const order = [...new Set(saved.order.filter(k => keys.includes(k)))];
        keys.forEach(k => { if (!order.includes(k)) order.push(k); });
        let hidden = [...new Set(saved.hidden.filter(k => keys.includes(k)))];
        // Never let a stale preference hide every available column.
        if (keys.length && hidden.length >= keys.length) hidden = [];
        return { registry, order, hidden };
    };

    const isHidden = key => state.hidden.includes(key);

    const bodyRows = table => {
        const tbody = table.querySelector(`tbody#${BODY_ID}`);
        return tbody ? [...tbody.children].filter(n => n.tagName === 'TR') : [];
    };

    // Apply visibility + order to the header row and every body row.
    const apply = () => {
        const table = getTable();
        if (!table) return;

        state.registry.forEach(col => {
            const hide = isHidden(col.key);
            table.querySelectorAll(`[data-col="${col.key}"]`).forEach(cell => cell.classList.toggle('d-none', hide));
        });

        [getHeaderRow(), ...bodyRows(table)].forEach(row => {
            if (!row) return;
            const cells = [...row.querySelectorAll(':scope > [data-col]')];
            if (cells.length < 2) return;
            // The cell after the customizable group (e.g. the actions cell) stays fixed;
            // re-inserting each cell before it rebuilds the group in the chosen order.
            const anchor = cells[cells.length - 1].nextElementSibling;
            state.order.forEach(key => {
                cells.filter(c => c.dataset.col === key).forEach(c => row.insertBefore(c, anchor));
            });
        });
    };

    const spriteBase = () => {
        const use = document.querySelector('svg.icon use');
        const href = use && (use.getAttribute('href') || use.getAttribute('xlink:href'));
        return href ? href.split('#')[0] : '';
    };

    const makeHandle = label => {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'wallet-columns__handle';
        btn.setAttribute('aria-label', moveLabelFormat.replace('{0}', () => label));
        const svg = document.createElementNS(SVG_NS, 'svg');
        svg.setAttribute('role', 'img');
        svg.setAttribute('class', 'icon icon-actions-drag');
        const use = document.createElementNS(SVG_NS, 'use');
        use.setAttribute('href', `${spriteBase()}#actions-drag`);
        svg.appendChild(use);
        btn.appendChild(svg);
        return btn;
    };

    const buildList = () => {
        const list = document.getElementById(LIST_ID);
        if (!list) return;
        const labels = Object.fromEntries(state.registry.map(c => [c.key, c.label]));
        list.textContent = '';
        state.order.forEach(key => {
            if (!(key in labels)) return;
            const li = document.createElement('li');
            li.className = 'wallet-columns__item';
            li.dataset.col = key;

            const check = document.createElement('div');
            check.className = 'form-check mb-0';
            const input = document.createElement('input');
            input.className = 'form-check-input';
            input.type = 'checkbox';
            input.id = `col-toggle-${key}`;
            input.checked = !isHidden(key);
            const label = document.createElement('label');
            label.className = 'form-check-label';
            label.htmlFor = input.id;
            label.textContent = labels[key];
            check.append(input, label);

            li.append(makeHandle(labels[key]), check);
            list.appendChild(li);
        });
    };

    const onToggle = event => {
        const input = event.target;
        const key = input.closest('li[data-col]')?.dataset.col;
        if (!key) return;
        if (input.checked) {
            state.hidden = state.hidden.filter(k => k !== key);
        } else if (state.order.filter(k => !isHidden(k)).length <= 1) {
            // Keep at least one column visible.
            input.checked = true;
            return;
        } else if (!state.hidden.includes(key)) {
            state.hidden.push(key);
        }
        persist();
        apply();
    };

    // Read the current DOM order of the list back into state, then persist + apply.
    const commitOrder = list => {
        state.order = [...list.querySelectorAll('li[data-col]')].map(el => el.dataset.col);
        persist();
        apply();
    };

    // Reorder a row by one step; keyboard-accessible alternative to dragging.
    const moveItem = (li, dir) => {
        const list = li.parentElement;
        if (dir < 0 && li.previousElementSibling) list.insertBefore(li, li.previousElementSibling);
        else if (dir > 0 && li.nextElementSibling) list.insertBefore(li.nextElementSibling, li);
        else return;
        commitOrder(list);
        li.querySelector('.wallet-columns__handle').focus();
    };

    const wire = list => {
        delegate('change', 'input[type="checkbox"]', onToggle, list);

        delegate('keydown', '.wallet-columns__handle', event => {
            const li = event.target.closest('li[data-col]');
            if (!li) return;
            if (event.key === 'ArrowUp') {
                event.preventDefault();
                moveItem(li, -1);
            } else if (event.key === 'ArrowDown') {
                event.preventDefault();
                moveItem(li, 1);
            }
        }, list);

        // Pointer-based dragging works with mouse, touch and pen (HTML5 drag events
        // never fire on touch). Unlike native drag there's no browser "ghost", so the
        // dragged row follows the pointer via a transform — making it obvious what is
        // being moved — while the other rows reflow underneath it.
        delegate('pointerdown', '.wallet-columns__handle', event => {
            if (event.button != null && event.button !== 0) return; // primary button / touch / pen only
            const li = event.target.closest('li[data-col]');
            if (!li) return;
            event.preventDefault();

            // Safety net: clear any leftover drag state (e.g. from an interrupted drag).
            list.querySelectorAll(`.${DRAGGING_CLASS}`).forEach(el => {
                el.classList.remove(DRAGGING_CLASS);
                el.style.transform = '';
            });

            // Track the dragged row in a local (not shared) so a second, concurrent
            // pointer (multi-touch, palm, fast re-tap) can't hijack an in-flight drag.
            const item = li;
            item.classList.add(DRAGGING_CLASS);
            const pointerId = event.pointerId;
            // startY is the pointer position at which the current transform baseline is 0.
            let startY = event.clientY;

            const onMove = e => {
                if (e.pointerId !== pointerId) return;
                item.style.transform = `translateY(${e.clientY - startY}px)`;

                // Once the dragged row's centre passes a neighbour's centre, swap them,
                // then nudge startY so the row stays visually pinned under the pointer.
                const rect = item.getBoundingClientRect();
                const mid = rect.top + rect.height / 2;
                const next = item.nextElementSibling;
                const prev = item.previousElementSibling;
                let moved = false;
                if (next) {
                    const nb = next.getBoundingClientRect();
                    if (mid > nb.top + nb.height / 2) { list.insertBefore(item, next.nextElementSibling); moved = true; }
                }
                if (!moved && prev) {
                    const pb = prev.getBoundingClientRect();
                    if (mid < pb.top + pb.height / 2) { list.insertBefore(item, prev); moved = true; }
                }
                if (moved) {
                    startY += item.getBoundingClientRect().top - rect.top;
                    item.style.transform = `translateY(${e.clientY - startY}px)`;
                }
            };
            // Listen on document so the drag always ends cleanly, even if pointer capture
            // is lost when the dragged row is re-inserted in the DOM (which would otherwise
            // leave the row's highlight stuck).
            const onUp = e => {
                if (e.pointerId !== pointerId) return;
                document.removeEventListener('pointermove', onMove);
                document.removeEventListener('pointerup', onUp);
                document.removeEventListener('pointercancel', onUp);
                item.style.transform = '';
                item.classList.remove(DRAGGING_CLASS);
                commitOrder(list);
            };
            document.addEventListener('pointermove', onMove);
            document.addEventListener('pointerup', onUp);
            document.addEventListener('pointercancel', onUp);
        }, list);

        delegate('click', `#${RESET_ID}`, () => {
            try {
                window.localStorage.removeItem(storageKey());
            } catch (e) { /* storage unavailable — degrade gracefully */ }
            state = resolveState();
            buildList();
            apply();
        });
    };

    const listEl = document.getElementById(LIST_ID);
    if (getTable() && getHeaderRow() && listEl) {
        storeId = listEl.dataset.storeId;
        moveLabelFormat = listEl.dataset.moveLabel || moveLabelFormat;
        state = resolveState();
        buildList();
        apply();
        wire(listEl);
        // Expose so the infinite-scroll loader can re-apply after appending rows.
        window.WalletTxColumns = { apply };
    }
})();
