if (!window.appTopItems) {
    window.appTopItems = {
        dataLoaded (model) {
            const id = `AppTopItems-${model.id}`;
            const series = model.salesCount;
            const labels = model.entries.map(e => e.title);
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
