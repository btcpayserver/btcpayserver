if (!window.storeLightningBalance) {
    window.storeLightningBalance = {
        dataLoaded (model) {
            const { storeId, cryptoCode, defaultCurrency, currencyData: { divisibility } }  = model;
            const id = `StoreLightningBalance-${storeId}`;
            const valueTransform = value => rate
                ? DashboardUtils.displayDefaultCurrency(value, rate, defaultCurrency, divisibility).toString()
                : value
            const labelCount = 6
            const chartOpts = {
                fullWidth: true,
                showArea: true,
                axisY: {
                    labelInterpolationFnc: valueTransform
                }
            };
            const baseUrl = model.dataUrl;
            let data = model;
            let rate = null;

            const render = data => {
                let { series, labels } = data;
                const currency = rate ? defaultCurrency : cryptoCode;
                document.querySelectorAll(`#${id} .currency`).forEach(c => c.innerText = currency)
                document.querySelectorAll(`#${id} [data-balance]`).forEach(c => {
                    const value = Number.parseFloat(c.dataset.balance);
                    c.innerText = valueTransform(value)
                });
                if (!series) return;

                const min = Math.min(...series);
                const max = Math.max(...series);
                const low = Math.max(min - ((max - min) / 5), 0);
                const tooltip = Chartist.plugins.tooltip2({
                    template: '{{value}}',
                    offset: {
                        x: 0,
                        y: -16
                    },
                    valueTransformFunction: valueTransform
                })
                const renderOpts = Object.assign({}, chartOpts, { low, plugins: [tooltip] });
                const pointCount = series.length;
                const labelEvery = pointCount / labelCount;
                // https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Intl/DateTimeFormat/DateTimeFormat
                const dateFormatter = new Intl.DateTimeFormat('default', { month: 'short', day: 'numeric' })
                const chart = new Chartist.Line(`#${id} .ct-chart`, {
                    labels: labels.map((date, i) => i % labelEvery == 0
                        ? dateFormatter.format(new Date(date))
                        : null),
                    series: [series]
                }, renderOpts);

                // prevent y-axis labels from getting cut off
                window.setTimeout(() => {
                    const yLabels = [...document.querySelectorAll('.ct-label.ct-vertical.ct-start')];
                    if (yLabels) {
                        const width = Math.max(...(yLabels.map(l => l.innerText.length * 7.5)));
                        const opts = Object.assign({}, renderOpts, {
                            axisY: Object.assign({}, renderOpts.axisY, { offset: width })
                        });
                        chart.update(null, opts);
                    }
                }, 0)
            };
            console.log(baseUrl)

            const update = async type => {
                const url = `${baseUrl}/${type}`;
                const response = await fetch(url);
                if (response.ok) {
                    data = await response.json();
                    render(data);
                }
            };

            render(data);

            delegate('change', `#${id} [name="StoreLightningBalancePeriod-${storeId}"]`, async e => {
                const type = e.target.value;
                await update(type);
            })
            delegate('change', `#${id} .currency-toggle input`, async e => {
                const { target } = e;
                if (target.value === defaultCurrency) {
                    rate = await DashboardUtils.fetchRate(`${cryptoCode}_${defaultCurrency}`);
                    if (rate) render(data);
                } else {
                    rate = null;
                    render(data);
                }
            });
        }
    };
}
