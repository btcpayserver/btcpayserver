let app, origData;
srv.sortBy = function (field) {
    for (let key in this.fieldViews) {
        if (this.fieldViews.hasOwnProperty(key)) {
            const sortedField = field === key;
            const fieldView = this.fieldViews[key];

            if (sortedField && (fieldView.sortBy === "" || fieldView.sortBy === "desc")) {
                fieldView.sortByTitle = "asc";
                fieldView.sortBy = "asc";
                fieldView.sortIconClass = "fa fa-sort-alpha-asc";
            }
            else if (sortedField && (fieldView.sortByTitle === "asc")) {
                fieldView.sortByTitle = "desc";
                fieldView.sortBy = "desc";
                fieldView.sortIconClass = "fa fa-sort-alpha-desc";
            }
            else {
                fieldView.sortByTitle = "";
                fieldView.sortBy = "";
                fieldView.sortIconClass = "fa fa-sort";
            }
        }
    }
    this.applySort();
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
                sortByTitle: "",
                sortIconClass: "fa fa-sort"
            };
        }
    }
};

document.addEventListener("DOMContentLoaded", () => {
    delegate("input", ".flatdtpicker", function () {
        // We don't use vue to bind dates, because VueJS break the flatpickr as soon as binding occurs.
        let to = document.getElementById("toDate").value
        let from = document.getElementById("fromDate").value

        if (!to || !from)
            return;

        from = moment(from).unix();
        to = moment(to).endOf('day').unix();

        srv.request.timePeriod.from = from;
        srv.request.timePeriod.to = to;
        fetchStoreReports();
    });

    delegate("click", "#exportCSV", downloadCSV);
    
    const $viewNameToggle = document.getElementById("ViewNameToggle")
    delegate("click", ".available-view", function (e) {
        e.preventDefault();
        const { view } = e.target.dataset;
        $viewNameToggle.innerText = view;
        document.querySelectorAll(".available-view").forEach($el => $el.classList.remove("custom-active"));
        e.target.classList.add("custom-active");
        srv.request.viewName = view;
        fetchStoreReports();
    });

    let to = new Date();
    let from = new Date(to.getTime() - 1000 * 60 * 60 * 24 * 30);
    var urlParams = new URLSearchParams(new URL(window.location).search);
    if (urlParams.has("from")) {
        from = new Date(parseInt(urlParams.get("from")) * 1000);
    }
    if (urlParams.has("to")) {
        to = new Date(parseInt(urlParams.get("to")) * 1000);
    }
    srv.request = srv.request || {};
    srv.request.timePeriod = srv.request.timePeriod || {};
    srv.request.timePeriod.to = moment(to).unix();
    srv.request.viewName = srv.request.viewName || "Payments";
    srv.request.timePeriod.from = moment(from).unix();
    srv.request.timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
    srv.result = { fields: [], values: [] };
    updateUIDateRange();
    app = new Vue({
        el: '#app',
        data() { return { srv } },
        methods: {
            hasChartData(chart) {
                return chart.rows.length || chart.hasGrandTotal;
            },
            titleCase(str, shorten) {
                const result = str.replace(/([A-Z])/g, " $1");
                const title = result.charAt(0).toUpperCase() + result.slice(1)
                return shorten && title.endsWith(' Amount') ? 'Amount' : title;
            },
            displayValue,
            displayDate
        }
    });
    fetchStoreReports();
});

const dtFormatter = new Intl.DateTimeFormat('default', { dateStyle: 'short', timeStyle: 'short' });

function displayDate(val) {
    if(!val){
        return val;
    }
    const date = new Date(val);
    return dtFormatter.format(date);
}

function displayValue(val) {
    return val && typeof val === "object" && typeof val.d === "number" ? new Decimal(val.v).toFixed(val.d) : val;
}

function updateUIDateRange() {
    document.getElementById("toDate")._flatpickr.setDate(moment.unix(srv.request.timePeriod.to).toDate());
    document.getElementById("fromDate")._flatpickr.setDate(moment.unix(srv.request.timePeriod.from).toDate());
}

// This function modify all the fields of a given type
function modifyFields(fields, data, type, action) {
    const fieldIndices = fields
        .map((f, i) => ({ i: i, type: f.type }))
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
    modifyFields(srv.result.fields, data, 'datetime', v => v? moment(v).format('YYYY-MM-DD hh:mm:ss'): v);
    const csv = Papa.unparse({ fields: srv.result.fields.map(f => f.name), data });
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    saveAs(blob, "export.csv");
}

async function fetchStoreReports() {
    const result = await fetch(window.location, {
        method: 'POST',
        headers: {
            'Accept': 'application/json',
            'Content-Type': 'application/json'
        },
        body: JSON.stringify(srv.request)
    });

    srv.result = await result.json();
    srv.dataUpdated();

    // Dates from API are UTC, convert them to local time
    modifyFields(srv.result.fields, srv.result.data, 'datetime', a => a? moment(a).format(): a);
    var urlParams = new URLSearchParams(new URL(window.location).search);
    urlParams.set("viewName", srv.request.viewName);
    urlParams.set("from", srv.request.timePeriod.from);
    urlParams.set("to", srv.request.timePeriod.to);
    history.replaceState(null, null, "?" + urlParams.toString());
    updateUIDateRange();

    srv.charts = [];
    for (let i = 0; i < srv.result.charts.length; i++) {
        const chart = srv.result.charts[i];
        const table = createTable(chart, srv.result.fields.map(f => f.name), srv.result.data);
        table.name = chart.name;
        srv.charts.push(table);
    }

    app.srv = srv;
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
