window.DashboardInterop = {
    _grid: null,
    _charts: {},
    _dotNetHelper: null,
    _changeBatch: null,

    // --- Gridstack integration ---
    initGrid: function (containerElement, dotNetHelper, editMode) {
        if (!containerElement || typeof GridStack === 'undefined') return;
        this.destroyGrid();
        this._dotNetHelper = dotNetHelper;

        var self = this;
        var grid = GridStack.init({
            column: 12,
            cellHeight: 146,
            margin: 8,
            float: true,
            animate: true,
            draggable: { handle: '.widget-drag-handle' },
            resizable: { handles: 'e,se,s,sw,w' },
            staticGrid: !editMode,
            columnOpts: {
                breakpoints: [{ w: 992, c: 1 }],
                breakpointForWindow: true,
                columnMax: 12
            }
        }, containerElement);

        // Batch change events with a short debounce to avoid multiple rapid calls
        grid.on('change', function (event, items) {
            if (!items || !self._dotNetHelper) return;
            // Debounce: collect changes, send once after 100ms of no activity
            if (self._changeBatch) clearTimeout(self._changeBatch);
            self._changeBatch = setTimeout(function () {
                self._changeBatch = null;
                var changes = [];
                var allNodes = grid.getGridItems();
                allNodes.forEach(function (el) {
                    var node = el.gridstackNode;
                    if (!node) return;
                    changes.push({
                        id: node.id || '',
                        x: node.x || 0,
                        y: node.y || 0,
                        w: node.w || 1,
                        h: node.h || 1
                    });
                });
                self._dotNetHelper.invokeMethodAsync('OnGridChanged', JSON.stringify(changes));
            }, 100);
        });

        // Destroy charts on resize start to prevent SVG artifacts
        grid.on('resizestart', function (event, el) {
            var chartElements = el.querySelectorAll('.ct-chart');
            chartElements.forEach(function (chartEl) {
                if (chartEl.id && self._charts[chartEl.id]) {
                    self._charts[chartEl.id].detach();
                    delete self._charts[chartEl.id];
                    chartEl.innerHTML = '';
                }
            });
        });

        this._grid = grid;
    },

    setEditMode: function (editMode) {
        if (!this._grid) return;
        this._grid.setStatic(!editMode);
    },

    destroyGrid: function () {
        if (this._changeBatch) {
            clearTimeout(this._changeBatch);
            this._changeBatch = null;
        }
        if (this._grid) {
            this._grid.destroy(false); // false = don't remove DOM elements (Blazor owns them)
            this._grid = null;
        }
        this._dotNetHelper = null;
    },

    addGridWidget: function (element) {
        if (!this._grid || !element) return;
        this._grid.makeWidget(element);
    },

    removeGridWidget: function (element) {
        if (!this._grid || !element) return;
        this._grid.removeWidget(element, false); // false = don't remove DOM
    },

    // --- Charts ---
    renderChart: function (elementId, type, labels, series, options) {
        var element = document.getElementById(elementId);
        if (!element || typeof Chartist === 'undefined') return;

        if (this._charts[elementId]) {
            this._charts[elementId].detach();
        }

        var chartData = { labels: labels, series: series };

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
