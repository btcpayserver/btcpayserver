window.DashboardInterop = {
    _sortableInstance: null,
    _charts: {},
    _resizeCleanups: {},

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

    // --- Multi-edge resize ---
    initResize: function (widgetInnerElement, placementId, currentColSpan, minCol, maxCol, currentRowSpan, minRow, maxRow, dotNetHelper) {
        var widgetElement = widgetInnerElement.closest('.dashboard-widget');
        if (!widgetElement) return;

        // Clean up previous bindings for this placement
        if (this._resizeCleanups[placementId]) {
            this._resizeCleanups[placementId]();
        }

        var self = this;
        var abortControllers = [];

        function setupEdge(handleSelector, axis) {
            var handle = widgetElement.querySelector(handleSelector);
            if (!handle) return;

            var controller = new AbortController();
            abortControllers.push(controller);

            handle.addEventListener('mousedown', function (e) {
                e.preventDefault();
                e.stopPropagation();

                var grid = widgetElement.closest('.dashboard-grid');
                if (!grid) return;

                var gridRect = grid.getBoundingClientRect();
                var gridStyle = window.getComputedStyle(grid);
                var gridCols = 12;
                var gap = parseFloat(gridStyle.gap) || parseFloat(gridStyle.columnGap) || 16;
                var rowGap = parseFloat(gridStyle.gap) || parseFloat(gridStyle.rowGap) || 16;
                var colWidth = (gridRect.width - (gap * (gridCols - 1))) / gridCols;

                // Compute row height from the widget's current rendered height / rowSpan
                var widgetRect = widgetElement.getBoundingClientRect();
                var baseRowHeight = currentRowSpan > 1
                    ? (widgetRect.height - rowGap * (currentRowSpan - 1)) / currentRowSpan
                    : widgetRect.height;

                var state = {
                    axis: axis,
                    startX: e.clientX,
                    startY: e.clientY,
                    startSpan: currentColSpan,
                    startRowSpan: currentRowSpan,
                    colWidth: colWidth,
                    gap: gap,
                    rowGap: rowGap,
                    baseRowHeight: baseRowHeight,
                    minCol: minCol,
                    maxCol: maxCol,
                    minRow: minRow,
                    maxRow: maxRow,
                    widgetElement: widgetElement,
                    widgetStartLeft: widgetRect.left,
                    gridLeft: gridRect.left,
                    currentPreviewCol: currentColSpan,
                    currentPreviewRow: currentRowSpan
                };

                var cursorStyle = axis === 'horizontal' ? 'col-resize' : 'row-resize';
                document.body.style.cursor = cursorStyle;
                document.body.style.userSelect = 'none';
                widgetElement.classList.add('widget-resizing');

                function onMove(ev) {
                    if (axis === 'horizontal') {
                        var dx = ev.clientX - state.startX;
                        // For left handle, invert the delta
                        var effectiveDx = (handleSelector === '.widget-resize-left') ? -dx : dx;
                        var colDelta = Math.round(effectiveDx / (state.colWidth + state.gap));
                        var newSpan = Math.max(state.minCol, Math.min(state.maxCol, state.startSpan + colDelta));
                        if (newSpan !== state.currentPreviewCol) {
                            state.currentPreviewCol = newSpan;
                            widgetElement.style.gridColumn = 'span ' + newSpan;
                        }
                    } else {
                        var dy = ev.clientY - state.startY;
                        var rowDelta = Math.round(dy / (state.baseRowHeight + state.rowGap));
                        var newRowSpan = Math.max(state.minRow, Math.min(state.maxRow, state.startRowSpan + rowDelta));
                        if (newRowSpan !== state.currentPreviewRow) {
                            state.currentPreviewRow = newRowSpan;
                            widgetElement.style.gridRow = 'span ' + newRowSpan;
                        }
                    }
                }

                function onUp(ev) {
                    document.removeEventListener('mousemove', onMove);
                    document.removeEventListener('mouseup', onUp);
                    document.body.style.cursor = '';
                    document.body.style.userSelect = '';
                    widgetElement.classList.remove('widget-resizing');

                    var finalCol = state.currentPreviewCol;
                    var finalRow = state.currentPreviewRow;

                    // Reset inline styles - Blazor will re-render
                    widgetElement.style.gridColumn = '';
                    widgetElement.style.gridRow = '';

                    if (finalCol !== state.startSpan || finalRow !== state.startRowSpan) {
                        dotNetHelper.invokeMethodAsync('OnWidgetResized', placementId, finalCol, finalRow);
                    }
                }

                document.addEventListener('mousemove', onMove);
                document.addEventListener('mouseup', onUp);
            }, { signal: controller.signal });
        }

        setupEdge('.widget-resize-right', 'horizontal');
        setupEdge('.widget-resize-left', 'horizontal');
        setupEdge('.widget-resize-bottom', 'vertical');

        this._resizeCleanups[placementId] = function () {
            abortControllers.forEach(function (c) { c.abort(); });
        };
    },

    cleanupResize: function (placementId) {
        if (this._resizeCleanups[placementId]) {
            this._resizeCleanups[placementId]();
            delete this._resizeCleanups[placementId];
        }
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
