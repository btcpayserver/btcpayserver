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
                var widgetRect = widgetElement.getBoundingClientRect();

                // Row height: use 120px as the snap unit (matches CSS grid-auto-rows min).
                // With minmax(120px, auto), actual rows may be taller than 120px due to
                // content, but we snap resize in 120px increments for predictable behavior.
                var baseRowHeight = 120;

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
                        var colDelta = Math.round(dx / (state.colWidth + state.gap));
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

        // series is already an array of arrays from C# (e.g. [[1,2,3]])
        var chartData = { labels: labels, series: series };

        // Compute low value from data for better chart scaling
        var low = 0;
        if (type === 'line' && series.length > 0) {
            var flatValues = series[0];
            if (flatValues && flatValues.length > 0) {
                var min = Math.min.apply(null, flatValues);
                var max = Math.max.apply(null, flatValues);
                low = Math.max(min - ((max - min) / 5), 0);
            }
        }

        var labelCount = 6;
        var pointCount = labels.length;
        var labelEvery = pointCount / labelCount;
        var dateFormatter = new Intl.DateTimeFormat('default', { month: 'short', day: 'numeric' });

        var tooltip = (typeof Chartist.plugins !== 'undefined' && Chartist.plugins.tooltip2)
            ? Chartist.plugins.tooltip2({
                template: '<div class="chartist-tooltip-value">{{value}}</div><div class="chartist-tooltip-line"></div>',
                offset: { x: 0, y: -16 }
            })
            : null;

        var defaultOptions = {
            low: low,
            showArea: type === 'line',
            fullWidth: true,
            axisX: {
                labelInterpolationFnc: function (date, i) {
                    return i % Math.ceil(labelEvery) === 0 ? dateFormatter.format(new Date(date)) : null;
                }
            },
            axisY: {
                showLabel: false,
                offset: 0
            },
            plugins: tooltip ? [tooltip] : []
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
    },

    // --- Export/Import ---
    downloadJson: function (filename, json) {
        var blob = new Blob([json], { type: 'application/json' });
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    },

    readFileAsText: function (inputElement) {
        return new Promise(function (resolve) {
            if (!inputElement || !inputElement.files || inputElement.files.length === 0) {
                resolve(null);
                return;
            }
            var reader = new FileReader();
            reader.onload = function () { resolve(reader.result); };
            reader.onerror = function () { resolve(null); };
            reader.readAsText(inputElement.files[0]);
        });
    }
};
