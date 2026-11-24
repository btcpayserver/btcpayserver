if (!window.appTopItems) {
    window.appTopItems = {
        dataLoaded (model) {
            const id = `AppTopItems-${model.id}`;
            const series = model.salesCount;
            // The tooltip inserts the labels as HTML through String.replace, which reads $ patterns,
            // and fills {{placeholders}} after the label is in
            const labels = model.entries.map(entry => {
                const label = document.createElement('span');
                label.textContent = entry.title;
                return label.innerHTML.replaceAll('$', '&#36;').replaceAll('{', '&#123;');
            });
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
