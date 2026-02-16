window.DashboardInterop = {
    _sortableInstance: null,
    _charts: {},

    initSortable: function (containerElement, dotNetHelper) {
        if (!containerElement || typeof Sortable === 'undefined') return;
        if (this._sortableInstance) {
            this._sortableInstance.destroy();
        }
        this._sortableInstance = new Sortable(containerElement, {
            animation: 150,
            handle: '.widget-drag-handle',
            ghostClass: 'widget-ghost',
            dragClass: 'widget-drag',
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

    renderChart: function (elementId, type, labels, series, options) {
        var element = document.getElementById(elementId);
        if (!element || typeof Chartist === 'undefined') return;

        // Destroy existing chart
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
