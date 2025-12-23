var app = null;
var eventAggregator = new Vue();

document.addEventListener("DOMContentLoaded",function (ev) {
    Vue.use(Toasted);

    app = new Vue({
        el: '#app',
        data: function () {
            return {
                srvModel: window.srvModel,
                connectionStatus: "",
                endDate: "",
                ended: false,
                endDiff: "",
                active: true,
                loading: false,
                timeoutState: "",
                customAmount: null,
                detailsShown: {}
            }
        },
        computed: {
            currency: function () {
                return this.srvModel.currency.toUpperCase();
            },
            settled: function () {
                return this.srvModel.amountDue <= 0;
            },
            lastUpdated: function () {
                return this.srvModel.lastUpdated && moment(this.srvModel.lastUpdated).calendar();
            },
            lastUpdatedDate: function () {
                return this.srvModel.lastUpdated && moment(this.srvModel.lastUpdated).format('MMMM Do YYYY, h:mm:ss a');
            },
            active: function () {
                return !this.ended;
            }
        },
        methods: {
            updateComputed: function () {
                if (this.srvModel.expiryDate) {
                    var endDateM = moment(this.srvModel.expiryDate);
                    this.endDate = endDateM.format('MMMM Do YYYY');
                    this.ended = endDateM.isBefore(moment());
                } else {
                    this.ended = false;
                    this.endDate = null;
                    this.endDiff = null;
                }

                if (!this.ended && this.srvModel.expiryDate) {
                    var mDiffD = moment(this.srvModel.expiryDate).diff(moment(), "days");
                    var mDiffH = moment(this.srvModel.expiryDate).diff(moment(), "hours");
                    var mDiffM = moment(this.srvModel.expiryDate).diff(moment(), "minutes");
                    var mDiffS = moment(this.srvModel.expiryDate).diff(moment(), "seconds");
                    this.endDiff = mDiffD > 0 ? mDiffD + " days" : mDiffH > 0 ? mDiffH + " hours" : mDiffM > 0 ? mDiffM + " minutes" : mDiffS > 0 ? mDiffS + " seconds" : "";
                }

                setTimeout(this.updateComputed, 1000);
            },
            setLoading: function (val) {
                this.loading = val;
                if (this.timeoutState) {
                    clearTimeout(this.timeoutState);
                }
            },
            pay: function (amount) {
                this.setLoading(true);
                var self = this;
                self.timeoutState = setTimeout(function () {
                    self.setLoading(false);
                }, 5000);

                eventAggregator.$emit("pay", amount);
            },
            cancelPayment: function (amount) {
                this.setLoading(true);
                var self = this;
                self.timeoutState = setTimeout(function () {
                    self.setLoading(false);
                }, 5000);
                eventAggregator.$emit("cancel-invoice", amount);
            },
            formatDate: function (date) {
                return moment(date).format('L h:mm A')
            },
            submitCustomAmountForm: function(e) {
                if (e) {
                    e.preventDefault();
                }
                if (this.srvModel.allowCustomPaymentAmounts && parseFloat(this.customAmount) < this.srvModel.amountDue){
                    this.pay(parseFloat(this.customAmount));
                } else {
                    this.pay();
                }
            },
            statusClass: function (state) {
                const [, status,, exceptionStatus] = state.match(/(\w*)\s?(\((\w*)\))?/) || [];
                switch (status) {
                    case "Expired":
                        switch (exceptionStatus) {
                            case "paidLate":
                            case "paidPartial":
                            case "paidOver":
                                return "unusual";
                            default:
                                return "expired";
                        }
                    default:
                        return status.toLowerCase();
                }
            },
            showDetails(invoiceId) {
                return this.detailsShown[invoiceId] === true;
            },
            toggleDetails(invoiceId) {
                if (this.detailsShown[invoiceId])
                    Vue.delete(this.detailsShown, invoiceId);
                else
                    Vue.set(this.detailsShown, invoiceId, true);
            }
        },
        mounted: function () {
            this.customAmount = (this.srvModel.amountDue || 0).noExponents();
            hubListener.connect();
            var self = this;
            var toastOptions = {
                iconPack: "fontawesome",
                theme: "bubble",
                duration: 10000
            };

            eventAggregator.$on("invoice-created", function (invoiceId) {
                self.setLoading(false);
                btcpay.appendAndShowInvoiceFrame(invoiceId);
            });
            eventAggregator.$on("invoice-cancelled", function (){
                self.setLoading(false);
                Vue.toasted.info('Payment cancelled', Object.assign({}, toastOptions), {
                    icon: "check"
                });
            });
            eventAggregator.$on("cancel-invoice-error", function () {
                self.setLoading(false);
                Vue.toasted.error("Error cancelling payment", Object.assign({}, toastOptions), {
                    icon: "exclamation-triangle"
                });
            });
            eventAggregator.$on("invoice-error", function (error) {
                self.setLoading(false);
                var msg = "";
                if (typeof error === "string") {
                    msg = error;
                } else if (!error) {
                    msg = "Unknown Error";
                } else {
                    msg = JSON.stringify(error);
                }
                Vue.toasted.error("Error creating invoice: " + msg, Object.assign({}, toastOptions), {
                    icon: "exclamation-triangle"
                });
            });
            eventAggregator.$on("payment-received", function (amount, currency, prettyPMI, pmi) {
                var onChain = pmi.endsWith('-CHAIN');
                var amountFormatted = parseFloat(amount).noExponents();
                var icon = onChain ? "plus" : "bolt";
                var title = "New payment of " + amountFormatted + " " + currency + " " + prettyPMI;
                Vue.toasted.success(title, Object.assign({}, toastOptions), { icon });
            });
            eventAggregator.$on("info-updated", function (model) {
                self.srvModel = model;
            });
            eventAggregator.$on("connection-pending", function () {
                self.connectionStatus = "pending";
            });
            eventAggregator.$on("connection-failed", function () {
                self.connectionStatus = "failed";
            });
            eventAggregator.$on("connection-lost", function () {
                self.connectionStatus = "connection lost";
            });
            this.updateComputed();
        }
    });
});

