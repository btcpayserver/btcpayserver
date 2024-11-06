if (!window.appSales) {
    window.appSales = {
        dataLoaded (model) {
            const id = `AppSales-${model.id}`;
            const appId = model.id;
            const period = model.period;
            const baseUrl = model.dataUrl;
            const data = model;

            const render = (data, period) => {
                document.querySelector(`#${id} .sales-count`).innerText = data.salesCount;
                
                const series = data.series.map(s => s.salesCount);
                const labels = data.series.map((s, i) => period === 'Month' ? (i % 5 === 0 ? s.label : '') : s.label);
                const min = Math.min(...series);
                const max = Math.max(...series);
                const low = min === max ? 0 : Math.max(min - ((max - min) / 5), 0);
                const tooltip = Chartist.plugins.tooltip2({
                    template: '<div class="chartist-tooltip-inner">Sales: {{value}}</div>',
                    offset: {
                        x: 0,
                        y: -8
                    }
                });
                new Chartist.Bar(`#${id} .ct-chart`, {
                    labels,
                    series: [series]
                }, {
                    low,
                    axisY: { onlyInteger: true },
                    plugins: [tooltip]
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
            
            function addEventListeners() {
                delegate('change', `#${id} [name="AppSalesPeriod-${appId}"]`, async e => {
                    const type = e.target.value;
                    await update(type);
                });
            }

            if (document.readyState === "loading") {
                window.addEventListener("DOMContentLoaded", addEventListeners);
            } else {
                addEventListeners();
            }
        }
    };
}
