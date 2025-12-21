if (!window.storeLightningBalance) {
    window.storeLightningBalance = {
        dataLoaded (model) {
            const { storeId, cryptoCode, defaultCurrency, currencyData: { divisibility } }  = model;
            const id = `StoreLightningBalance-${storeId}`;
            const valueTransform = value => rate ? DashboardUtils.displayDefaultCurrency(value, rate, defaultCurrency, divisibility) : value
            const labelCount = 6
            const tooltip = Chartist.plugins.tooltip2({
                template: '<div class="chartist-tooltip-value">{{value}}</div><div class="chartist-tooltip-line"></div>',
                offset: {
                    x: 0,
                    y: -16
                },
                valueTransformFunction(value, label) {
                    return valueTransform(value) + ' ' + (rate ? defaultCurrency : cryptoCode)
                }
            })
            // https://developer.mozilla.org/en-US/docs/Web/JavaScript/Reference/Global_Objects/Intl/DateTimeFormat/DateTimeFormat
            const dateFormatter = new Intl.DateTimeFormat('default', { month: 'short', day: 'numeric' })
            const chartOpts = {
                fullWidth: true,
                showArea: true,
                axisY: {
                    showLabel: false,
                    offset: 0
                },
                plugins: [tooltip]
            };
            const baseUrl = model.dataUrl;
            let data = model;
            let rate = null;

            const render = data => {
                let { series, labels } = data;
                const currency = rate ? defaultCurrency : cryptoCode;
                document.querySelectorAll(`#${id} .currency`).forEach(c => c.innerText = currency)
                document.querySelectorAll(`#${id} [data-balance]`).forEach(c => {
                    c.innerText = valueTransform(c.dataset.balance)
                });
                if (!series) return;

                const min = Math.min(...series);
                const max = Math.max(...series);
                const low = Math.max(min - ((max - min) / 5), 0);
                const renderOpts = Object.assign({}, chartOpts, { low, axisX: {
                    labelInterpolationFnc(date, i) {
                        return i % labelEvery == 0 ? dateFormatter.format(new Date(date)) : null
                    }
                } });
                const pointCount = series.length;
                const labelEvery = pointCount / labelCount;
                const chart = new Chartist.Line(`#${id} .ct-chart`, {
                    labels: labels,
                    series: [series]
                }, renderOpts);
            };

            const update = async type => {
                const url = `${baseUrl}/${type}`;
                const response = await fetch(url);
                if (response.ok) {
                    data = await response.json();
                    render(data);
                }
            };

            render(data);

            function addEventListeners() {
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

            if (document.readyState === "loading") {
                window.addEventListener("DOMContentLoaded", addEventListeners);
            } else {
                addEventListeners();
            }
        }
    };
}
