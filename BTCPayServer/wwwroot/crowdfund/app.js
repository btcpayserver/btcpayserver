var app = null;
var eventAggregator = new Vue();
window.onload = function (ev) {
    Vue.use(Toasted);
    app = new Vue({
        el: '#app',
        data: function(){
            return {
                srvModel: window.srvModel,
                connectionStatus: "",
                endDate: "",
                startDate: "",
                startDateRelativeTime: "",
                endDateRelativeTime: "",
                started: false,
                ended: false,
                contributeModalOpen: false,
                thankYouModalOpen: false
            }
        },
        computed: {
            targetCurrency: function(){
                return this.srvModel.targetCurrency.toUpperCase();
            }
        },
        methods: {
            updateComputed: function () {
                if (this.srvModel.endDate) {
                    var endDateM = moment(this.srvModel.endDate);
                    this.endDate = endDateM.format('MMMM Do YYYY');
                    this.endDateRelativeTime = endDateM.fromNow();
                    this.ended = endDateM.isBefore(moment());
                }else{
                    this.ended = false;
                }

                if (this.srvModel.startDate) {
                    var startDateM = moment(this.srvModel.startDate);
                    this.startDate = startDateM.format('MMMM Do YYYY');
                    this.startDateRelativeTime = startDateM.fromNow();
                    this.started = startDateM.isBefore(moment());
                }else{
                    this.started = true;
                }
                setTimeout(this.updateComputed, 1000);
            },
            submitModalContribute: function(e){
                debugger;
                this.$refs.modalContribute.onContributeFormSubmit(e);
            }
        },
        mounted: function () {
            hubListener.connect();
            var self = this;
            eventAggregator.$on("invoice-created", function (invoiceId) {
                btcpay.setApiUrlPrefix(window.location.origin);
                btcpay.showInvoice(invoiceId);
                btcpay.showFrame();

                self.contributeModalOpen = false;
            });
            btcpay.onModalWillLeave = function(){
                self.thankYouModalOpen = true;
            };
            eventAggregator.$on("payment-received", function (amount) {
                console.warn("AAAAAA", amount);
                Vue.toasted.show('New payment of ' + amount+ " BTC registered");
            });
            eventAggregator.$on("info-updated", function (model) {
                this.srvModel = model;
            });
            eventAggregator.$on("connection-pending", function () {
                this.connectionStatus = "pending";
            });
            eventAggregator.$on("connection-failed", function () {
                this.connectionStatus = "failed";
            });
            eventAggregator.$on("connection-lost", function () {
                this.connectionStatus = "connection lost";
            });
            this.updateComputed();
        }
    });
};

