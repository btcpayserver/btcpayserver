var app;
var origData;

srv.sortBy = function (field) {
    for (let key in this.fieldViews) {
        if (this.fieldViews.hasOwnProperty(key)) {
            var sortedField = field == key;
            var fieldView = this.fieldViews[key];

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
    var fieldIndex;
    var fieldView;
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
    var sortType = fieldView.sortBy === "desc" ? 1 : -1;
    srv.result.data.sort(function (a, b) {
        var aVal = a[fieldIndex];
        var bVal = b[fieldIndex];
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

$(function () {
    $(".flatdtpicker").on("input", function () {
        // We don't use vue to bind dates, because VueJS break the flatpickr as soon as binding occurs.
        var to = $("#toDate").val();
        var from = $("#fromDate").val();

        if (!to || !from)
            return;

        from = moment(from).unix();
        to = moment(to).endOf('day').unix();

        srv.request.timePeriod.from = from;
        srv.request.timePeriod.to = to;
        fetchStoreReports();
    });

    $("#exportCSV").on("click", downloadCSV);
    $(".available-view").on("click", function () {
        var view = $(this).data("view");
        $("#ViewNameToggle").text(view);
        $(".available-view").removeClass("custom-active");
        $(this).addClass("custom-active");
        srv.request.viewName = view;
        fetchStoreReports();
    });

    var to = new Date();
    var from = new Date(to.getTime() - 1000 * 60 * 60 * 24 * 30);
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
        data: { srv: srv }
    });
    fetchStoreReports();
});

function updateUIDateRange() {
    flatpickr("#toDate").setDate(moment.unix(srv.request.timePeriod.to).toDate());
    flatpickr("#fromDate").setDate(moment.unix(srv.request.timePeriod.from).toDate());
}

// This function modify all the fields of a given type
function modifyFields(fields, data, type, action) {
    var fieldIndices = fields.map((f, i) => ({ i: i, type: f.type })).filter(f => f.type == type).map(f => f.i);
    if (fieldIndices.length === 0)
        return;
    for (var i = 0; i < data.length; i++) {
        for (var f = 0; f < fieldIndices.length; f++) {
            data[i][fieldIndices[f]] = action(data[i][fieldIndices[f]]);
        }
    }
}
function downloadCSV() {
    if (!origData)
        return;
    var data = clone(origData);

    // Convert ISO8601 dates to YYYY-MM-DD HH:mm:ss so the CSV easily integrate with Excel
    modifyFields(srv.result.fields, data, 'datetime', (v) => moment(v).format('YYYY-MM-DD hh:mm:ss'));
    var csv = Papa.unparse(
        {
            fields: srv.result.fields.map(f => f.name),
            data: data
        });

    var blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    saveAs(blob, "export.csv");
}

async function fetchStoreReports() {
    var result = await fetch(window.location, {
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
    modifyFields(srv.result.fields, srv.result.data, 'datetime', (a) => moment(a).format())
    updateUIDateRange();

    srv.charts = [];
    for (var i = 0; i < srv.result.charts.length; i++) {
        var chart = srv.result.charts[i];
        var table = createTable(chart, srv.result.fields.map(f => f.name), srv.result.data);
        table.name = chart.name;
        srv.charts.push(table);
    }

    app.srv = srv;
}

function getRandomValue(arr) {
    return arr[Math.floor(Math.random() * arr.length)];
}

function getRandomNumber(min, max) {
    return Math.random() * (max - min) + min;
}

function generateRandomRows(numRows) {
    const regions = ["Russia", "France", "Japan", "Portugal"];
    const cryptos = ["BTC", "LTC", "DASH", "DOGE"];
    const paymentTypes = ["On-Chain", "Off-Chain"];
    const rows = [];

    for (let i = 0; i < numRows; i++) {
        const region = getRandomValue(regions);
        const crypto = getRandomValue(cryptos);
        const paymentType = getRandomValue(paymentTypes);
        const amount = getRandomNumber(10, 5000);
        const cryptoAmount = getRandomNumber(0.1, 2.5);

        const row = [region, crypto, paymentType, amount, cryptoAmount];
        rows.push(row);
    }

    return rows;
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
