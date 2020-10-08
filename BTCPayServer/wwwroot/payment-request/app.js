var app = null;
var eventAggregator = new Vue();

function addLoadEvent(func) {
    var oldonload = window.onload;
    if (typeof window.onload != 'function') {
        window.onload = func;
    } else {
        window.onload = function () {
            if (oldonload) {
                oldonload();
            }
            func();
        }
    }
}

addLoadEvent(function (ev) {
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
                lastUpdated: "",
                loading: false,
                timeoutState: "",
                customAmount: null
            }
        },
        computed: {
            currency: function () {
                return this.srvModel.currency.toUpperCase();
            },
            settled: function () {
                return this.srvModel.amountDue <= 0;
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

                this.lastUpdated = moment(this.srvModel.lastUpdated).calendar();
                this.active = !this.ended;
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
            formatPaymentMethod: function (str) {

                if (str.endsWith("LightningLike")) {
                    return str.replace("LightningLike", "Lightning")
                }
                return str;

            },
            print:function(){
                window.print();
            },
            submitCustomAmountForm : function(e){
                if (e) {
                    e.preventDefault();
                }
                if(this.srvModel.allowCustomPaymentAmounts && parseFloat(this.customAmount) < this.srvModel.amountDue){
                    this.pay(parseFloat(this.customAmount));
                }else{
                    this.pay();
                }
            }
        },
        mounted: function () {

            this.customAmount = (this.srvModel.amountDue || 0).noExponents();
            hubListener.connect();
            var self = this;
            eventAggregator.$on("invoice-created", function (invoiceId) {
                self.setLoading(false);
                btcpay.showInvoice(invoiceId);
                btcpay.showFrame();
            });
            eventAggregator.$on("invoice-cancelled", function(){
                self.setLoading(false);
                Vue.toasted.show('Payment cancelled', {
                    iconPack: "fontawesome",
                    icon: "check",
                    duration: 10000
                });
            });
            eventAggregator.$on("cancel-invoice-error", function (error) {
                self.setLoading(false);
                Vue.toasted.show("Error cancelling payment", {
                    iconPack: "fontawesome",
                    icon: "exclamation-triangle",
                    fullWidth: false,
                    theme: "bubble",
                    type: "error",
                    position: "top-center",
                    duration: 10000
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

                Vue.toasted.show("Error creating invoice: " + msg, {
                    iconPack: "fontawesome",
                    icon: "exclamation-triangle",
                    fullWidth: false,
                    theme: "bubble",
                    type: "error",
                    position: "top-center",
                    duration: 10000
                });
            });
            eventAggregator.$on("payment-received", function (amount, cryptoCode, type) {
                var onChain = type.toLowerCase() === "btclike";
                amount = parseFloat(amount).noExponents();
                if (onChain) {
                    Vue.toasted.show('New payment of ' + amount + " " + cryptoCode + " " + (onChain ? "On Chain" : "LN "), {
                        iconPack: "fontawesome",
                        icon: "plus",
                        duration: 10000
                    });
                } else {
                    Vue.toasted.show('New payment of ' + amount + " " + cryptoCode + " " + (onChain ? "On Chain" : "LN "), {
                        iconPack: "fontawesome",
                        icon: "bolt",
                        duration: 10000
                    });
                }


            });
            eventAggregator.$on("info-updated", function (model) {
                console.warn("UPDATED", self.srvModel, arguments);
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

