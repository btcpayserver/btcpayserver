if (!window.appTopItems) {
    window.appTopItems = {
        dataLoaded (model) {
            const id = `AppTopItems-${model.id}`;
            const series = model.salesCount;
            new Chartist.Bar(`#${id} .ct-chart`, { series }, {
                distributeSeries: true,
                horizontalBars: true,
                showLabel: false,
                stackBars: true,
                axisY: {
                    offset: 0
                }
            });
        }
    };
}
