if (!window.appTopItems) {
    window.appTopItems = {
        dataLoaded (model) {
            const id = `AppTopItems-${model.id}`;
            const series = model.salesCount;
            // Titles are sanitized server-side. Prevent the tooltip's String.replace
            // calls from interpreting replacement patterns or template placeholders.
            const labels = model.entries.map(entry =>
                entry.title.replaceAll('$', '&#36;').replaceAll('{', '&#123;'));
            const tooltip = Chartist.plugins.tooltip2({
                template: '<div class="chartist-tooltip-inner">{{meta}} - Sales: {{value}}</div>',
                offset: {
                    x: 0,
                    y: -8
                }
            });
            new Chartist.Bar(`#${id} .ct-chart`, { series, labels }, {
                distributeSeries: true,
                horizontalBars: true,
                showLabel: false,
                stackBars: true,
                axisY: {
                    offset: 0
                },
                plugins: [tooltip]
            });
        }
    };
}
