var app = null;
var eventAggregator = new Vue();

function addLoadEvent(func) {
    var oldonload = window.onload;
    if (typeof window.onload != 'function') {
        window.onload = func;
    } else {
        window.onload = function() {
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
                contributeModalOpen: false
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
            eventAggregator.$on("payment-received", function (amount, cryptoCode, type) {
                var onChain = type.toLowerCase() === "btclike";
                playRandomQuakeSound();
                fireworks();
                if(onChain){
                    Vue.toasted.show('New payment of ' + amount+ " "+ cryptoCode + " " + (onChain? "On Chain": "LN "), {
                        iconPack: "fontawesome",
                        icon: "plus",
                        duration: 10000
                    } );
                }else{
                    Vue.toasted.show('New payment of ' + amount+ " "+ cryptoCode + " " + (onChain? "On Chain": "LN "), {
                        iconPack: "fontawesome",
                        icon: "bolt",
                        duration: 10000
                    } );
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

