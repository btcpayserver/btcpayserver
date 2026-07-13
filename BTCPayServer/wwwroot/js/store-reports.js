let app, origData;
srv.sortBy = function (field, event) {
    for (let key in this.fieldViews) {
        if (this.fieldViews.hasOwnProperty(key)) {
            const sortedField = field === key;
            const fieldView = this.fieldViews[key];

            if (sortedField && (fieldView.sortBy === "" || fieldView.sortBy === "desc")) {
                fieldView.sortByTitle = "asc";
                fieldView.sortBy = "asc";
            } else if (sortedField && (fieldView.sortByTitle === "asc")) {
                fieldView.sortByTitle = "desc";
                fieldView.sortBy = "desc";
            } else {
                fieldView.sortByTitle = "";
                fieldView.sortBy = "";
            }
        }
    }
    this.applySort();
    document.querySelectorAll('.sort-column').forEach($a => {
        $a.innerHTML = $a.innerHTML.replace(/#actions-sort-(asc|desc)"/, '#actions-sort"')
    })
    const {sort} = event.currentTarget.dataset;
    const next = sort === '' || sort === 'desc' ? 'asc' : 'desc';
    const icon = event.currentTarget.querySelector('svg');
    if (icon)
        icon.setAttribute('href', `#actions-sort-${next}`);
}

srv.applySort = function () {
    let fieldIndex, fieldView;
    for (let key in this.fieldViews) {
        if (this.fieldViews.hasOwnProperty(key)) {
            fieldView = this.fieldViews[key];
            if (fieldView.sortBy !== "") {
                fieldIndex = this.result.fields.findIndex((a) => a.name === key);
                break;
            }
            fieldView = null;
        }
    }
    if (!fieldView)
        return;
    const sortType = fieldView.sortBy === "desc" ? 1 : -1;
    srv.result.data.sort(function (a, b) {
        const aVal = a[fieldIndex];
        const bVal = b[fieldIndex];
        if (aVal === bVal) return 0;
        if (aVal === null) return 1 * sortType;
        if (bVal === null) return -1 * sortType;
        if (aVal > bVal) return 1 * sortType;
        return -1 * sortType;
    });
};
srv.dataUpdated = function () {
    this.updateFieldViews();
    origData = clone(this.result.data);
    this.applySort();
};
srv.updateFieldViews = function () {
    this.fieldViews = this.fieldViews || {};

    // First we remove the fieldViews that doesn't apply anymore
    for (let key in this.fieldViews) {
        if (this.fieldViews.hasOwnProperty(key)) {
            if (!this.result.fields.find(i => i.name === key))
                delete this.fieldViews[key];
        }
    }

    // Then we add those that are missing
    for (let i = 0; i < this.result.fields.length; i++) {
        const field = this.result.fields[i];
        if (!this.fieldViews.hasOwnProperty(field.name)) {
            this.fieldViews[field.name] =
                {
                    sortBy: "",
                    sortByTitle: ""
                };
        }
    }
};
document.addEventListener("DOMContentLoaded", () => {
    delegate("click", "[data-action='exportCSV']", downloadCSV);
    srv.result = srv.result || {fields: [], data: [], charts: []};
    srv.dataUpdated();
    modifyFields(srv.result.fields, srv.result.data, 'datetime', a => a ? moment(a).format() : a);
    srv.charts = [];
    for (let i = 0; i < srv.result.charts.length; i++) {
        const chart = srv.result.charts[i];
        const table = createTable(chart, srv.result.fields.map(f => f.name), srv.result.data);
        table.name = chart.name;
        srv.charts.push(table);
    }
    app = new Vue({
        el: '#app',
        data() {
            return {srv};
        },
        methods: {
            hasChartData(chart) {
                return chart && (chart.rows.length || chart.hasGrandTotal);
            },
            titleCase(str, shorten) {
                const result = str.replace(/([a-z])([A-Z])/g, '$1 $2'); // only split camelCase
                const title = result.charAt(0).toUpperCase() + result.slice(1);
                return shorten && title.endsWith(' Amount') ? 'Amount' : title;
            },
            displayValue,
            displayDate
        }
    });
});

const dtFormatter = getDateFormatter();

function displayDate(val) {
    if (!val) {
        return val;
    }
    const date = new Date(val);
    return dtFormatter.format(date);
}

function displayValue(val) {
    return val && typeof val === "object" && typeof val.d === "number" ? new Decimal(val.v).toFixed(val.d) : val;
}

// This function modify all the fields of a given type
function modifyFields(fields, data, type, action) {
    const fieldIndices = fields
        .map((f, i) => ({i: i, type: f.type}))
        .filter(f => f.type === type)
        .map(f => f.i);
    if (fieldIndices.length === 0)
        return;
    for (let i = 0; i < data.length; i++) {
        for (let f = 0; f < fieldIndices.length; f++) {
            data[i][fieldIndices[f]] = action(data[i][fieldIndices[f]]);
        }
    }
}

function downloadCSV() {
    if (!origData) return;
    const data = clone(origData);

    // Convert ISO8601 dates to YYYY-MM-DD HH:mm:ss so the CSV easily integrate with Excel
    modifyFields(srv.result.fields, data, 'amount', displayValue)
    modifyFields(srv.result.fields, data, 'datetime', v => v ? moment(v).format('YYYY-MM-DD HH:mm:ss') : v);
    const csv = Papa.unparse({fields: srv.result.fields.map(f => f.name), data});
    const blob = new Blob([csv], {type: 'text/csv;charset=utf-8;'});
    saveAs(blob, "export.csv");
}

function getInvoiceUrl(value) {
    if (!value)
        return;
    return srv.invoiceTemplateUrl.replace("INVOICE_ID", value);
}

window.getInvoiceUrl = getInvoiceUrl;

function getExplorerUrl(tx_id, cryptoCode) {
    if (!tx_id || !cryptoCode)
        return null;
    var explorer = srv.explorerTemplateUrls[cryptoCode];
    if (!explorer)
        return null;
    return explorer.replace("TX_ID", tx_id);
}

window.getExplorerUrl = getExplorerUrl;
