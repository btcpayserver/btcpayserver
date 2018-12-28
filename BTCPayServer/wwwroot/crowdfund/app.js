var app = null;
var eventAggregator = new Vue();
window.onload = function (ev) {
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
                contributionForm: { email: "", amount: 0}
            }
        },
        computed: {},
        methods: {
            updateComputed: function () {
                if (this.srvModel.endDate) {
                    var endDateM = moment(this.srvModel.endDate);
                    this.endDate = endDateM.format('MMMM Do YYYY');
                    this.endDateRelativeTime = endDateM.fromNow();
                    this.ended = endDateM.isBefore(moment());
                }else{
                    this.ended = true;
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
            onContributeFormSubmit: function(e){
                if(e){
                    e.preventDefault();
                }
                eventAggregator.$emit("contribute", this.contributionForm);                
            }
        },
        mounted: function () {
            hubListener.connect();
            eventAggregator.$on("invoice-created", function (invoiceId) {
                btcpay.setApiUrlPrefix(window.location.origin);
                btcpay.showInvoice(invoiceId);
                btcpay.showFrame();
            }); 
            eventAggregator.$on("payment-received", function (amount) {
                console.warn("AAAAAA", amount);
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

