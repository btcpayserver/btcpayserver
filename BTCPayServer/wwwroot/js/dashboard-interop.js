window.DashboardInterop = {
    _sortableInstance: null,
    _charts: {},
    _resizeState: null,

    initSortable: function (containerElement, dotNetHelper) {
        if (!containerElement || typeof Sortable === 'undefined') return;
        if (this._sortableInstance) {
            this._sortableInstance.destroy();
        }
        this._sortableInstance = new Sortable(containerElement, {
            animation: 200,
            handle: '.widget-drag-handle',
            ghostClass: 'widget-ghost',
            dragClass: 'widget-drag',
            chosenClass: 'widget-chosen',
            forceFallback: false,
            swapThreshold: 0.65,
            direction: 'vertical',
            onEnd: function (evt) {
                // Revert the DOM change - let Blazor handle the reorder
                var parent = evt.from;
                if (evt.oldIndex < evt.newIndex) {
                    parent.insertBefore(evt.item, parent.children[evt.oldIndex]);
                } else {
                    parent.insertBefore(evt.item, parent.children[evt.oldIndex + 1]);
                }
                dotNetHelper.invokeMethodAsync('OnWidgetReordered', evt.oldIndex, evt.newIndex);
            }
        });
    },

    destroySortable: function () {
        if (this._sortableInstance) {
            this._sortableInstance.destroy();
            this._sortableInstance = null;
        }
    },

    // --- Edge resize ---
    initResize: function (widgetInnerElement, placementId, currentColSpan, minCol, maxCol, dotNetHelper) {
        // Walk up from the inner .widget to the outer .dashboard-widget
        var widgetElement = widgetInnerElement.closest('.dashboard-widget');
        if (!widgetElement) return;
        var handle = widgetElement.querySelector('.widget-resize-handle');
        if (!handle) return;
        // Avoid double-binding
        if (handle._resizeBound) return;
        handle._resizeBound = true;

        var self = this;

        handle.addEventListener('mousedown', function (e) {
            e.preventDefault();
            e.stopPropagation();

            var grid = widgetElement.closest('.dashboard-grid');
            if (!grid) return;

            var gridRect = grid.getBoundingClientRect();
            var gridStyle = window.getComputedStyle(grid);
            var gridCols = 12;
            var gap = parseFloat(gridStyle.gap) || parseFloat(gridStyle.columnGap) || 16;
            var colWidth = (gridRect.width - (gap * (gridCols - 1))) / gridCols;

            var startX = e.clientX;
            var startSpan = currentColSpan;

            self._resizeState = {
                placementId: placementId,
                startX: startX,
                startSpan: startSpan,
                colWidth: colWidth,
                gap: gap,
                minCol: minCol,
                maxCol: maxCol,
                widgetElement: widgetElement,
                dotNetHelper: dotNetHelper
            };

            document.addEventListener('mousemove', self._onResizeMove);
            document.addEventListener('mouseup', self._onResizeEnd);
            document.body.style.cursor = 'col-resize';
            document.body.style.userSelect = 'none';
            widgetElement.classList.add('widget-resizing');
        });
    },

    _onResizeMove: function (e) {
        var state = window.DashboardInterop._resizeState;
        if (!state) return;

        var dx = e.clientX - state.startX;
        var colDelta = Math.round(dx / (state.colWidth + state.gap));
        var newSpan = Math.max(state.minCol, Math.min(state.maxCol, state.startSpan + colDelta));

        if (newSpan !== state.currentPreview) {
            state.currentPreview = newSpan;
            state.widgetElement.style.gridColumn = 'span ' + newSpan;
        }
    },

    _onResizeEnd: function (e) {
        var self = window.DashboardInterop;
        var state = self._resizeState;
        if (!state) return;

        document.removeEventListener('mousemove', self._onResizeMove);
        document.removeEventListener('mouseup', self._onResizeEnd);
        document.body.style.cursor = '';
        document.body.style.userSelect = '';
        state.widgetElement.classList.remove('widget-resizing');

        var dx = e.clientX - state.startX;
        var colDelta = Math.round(dx / (state.colWidth + state.gap));
        var newSpan = Math.max(state.minCol, Math.min(state.maxCol, state.startSpan + colDelta));

        // Reset inline style - Blazor will re-render with the correct span
        state.widgetElement.style.gridColumn = '';

        if (newSpan !== state.startSpan) {
            state.dotNetHelper.invokeMethodAsync('OnWidgetResized', state.placementId, newSpan);
        }

        self._resizeState = null;
    },

    // --- Charts ---
    renderChart: function (elementId, type, labels, series, options) {
        var element = document.getElementById(elementId);
        if (!element || typeof Chartist === 'undefined') return;

        if (this._charts[elementId]) {
            this._charts[elementId].detach();
        }

        var chartData = { labels: labels, series: [series] };
        var defaultOptions = {
            low: 0,
            showArea: type === 'line',
            fullWidth: true,
            axisX: { showGrid: false },
            axisY: { showGrid: true, offset: 40 },
            plugins: typeof Chartist.plugins !== 'undefined' && Chartist.plugins.tooltip2
                ? [Chartist.plugins.tooltip2()]
                : []
        };

        var mergedOptions = Object.assign({}, defaultOptions, options || {});

        if (type === 'line') {
            this._charts[elementId] = new Chartist.Line('#' + elementId, chartData, mergedOptions);
        } else if (type === 'bar') {
            this._charts[elementId] = new Chartist.Bar('#' + elementId, chartData, mergedOptions);
        }
    },

    destroyChart: function (elementId) {
        if (this._charts[elementId]) {
            this._charts[elementId].detach();
            delete this._charts[elementId];
        }
    }
};
