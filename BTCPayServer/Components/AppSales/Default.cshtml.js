if (!window.appSales) {
    window.appSales = {
        dataLoaded (model) {
            const id = `AppSales-${model.id}`;
            const appId = model.id;
            const period = model.period;
            const baseUrl = model.dataUrl;
            const data = model;

            const render = (data, period) => {
                const series = data.series.map(s => s.salesCount);
                const labels = data.series.map((s, i) => period === 'Month' ? (i % 5 === 0 ? s.label : '') : s.label);
                const min = Math.min(...series);
                const max = Math.max(...series);
                const low = min === max ? 0 : Math.max(min - ((max - min) / 5), 0);
                document.querySelectorAll(`#${id} .sales-count`).innerText = data.salesCount;
                new Chartist.Bar(`#${id} .ct-chart`, {
                    labels,
                    series: [series]
                }, {
                    low
                });
            };

            render(data, period);

            const update = async period => {
                const url = `${baseUrl}/${period}`;
                const response = await fetch(url);
                if (response.ok) {
                    const data = await response.json();
                    render(data, period);
                }
            };

            delegate('change', `#${id} [name="AppSalesPeriod-${appId}"]`, async e => {
                const type = e.target.value;
                await update(type);
            });
        }
    };
}
