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
                contributeModalOpen: false,
                endDiff: "",
                startDiff: "",
                active: true
                
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
                if(this.started && !this.ended && this.srvModel.endDate){
                    var mDiffD =  moment(this.srvModel.endDate).diff(moment(), "days");
                    var mDiffH =  moment(this.srvModel.endDate).diff(moment(), "hours");
                    var mDiffM =  moment(this.srvModel.endDate).diff(moment(), "minutes");
                    var mDiffS =  moment(this.srvModel.endDate).diff(moment(), "seconds");
                    this.endDiff =  mDiffD > 0? mDiffD + " Days" : mDiffH> 0? mDiffH + " Hours" : mDiffM> 0? mDiffM+ " Minutes" : mDiffS> 0? mDiffS + " Seconds": ""; 
                }
                if(!this.started && this.srvModel.startDate){
                    var mDiffD =  moment(this.srvModel.startDate).diff(moment(), "days");
                    var mDiffH =  moment(this.srvModel.startDate).diff(moment(), "hours");
                    var mDiffM =  moment(this.srvModel.startDate).diff(moment(), "minutes");
                    var mDiffS =  moment(this.srvModel.startDate).diff(moment(), "seconds");
                    this.startDiff =  mDiffD > 0? mDiffD + " Days" : mDiffH> 0? mDiffH + " Hours" : mDiffM> 0? mDiffM+ " Minutes" : mDiffS> 0? mDiffS + " Seconds": "";
                }
                this.active = this.started && !this.ended;
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
                if(this.srvModel.soundsEnabled) {
                    playRandomQuakeSound();
                }
                if(this.srvModel.animationsEnabled) {
                    fireworks();
                }
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
            if(srvModel.disqusEnabled){
                window.disqus_config = function () {
                    // Replace PAGE_URL with your page's canonical URL variable
                    this.page.url = window.location.href;

                    // Replace PAGE_IDENTIFIER with your page's unique identifier variable
                    this.page.identifier = self.srvModel.appId;
                };

                (function() {  // REQUIRED CONFIGURATION VARIABLE: EDIT THE SHORTNAME BELOW
                    var d = document, s = d.createElement('script');

                    // IMPORTANT: Replace EXAMPLE with your forum shortname!
                    s.src = "https://"+self.srvModel.disqusShortname+".disqus.com/embed.js";
                    s.async= true;
                    s.setAttribute('data-timestamp', +new Date());
                    (d.head || d.body).appendChild(s);
                    
                    var s2 = d.createElement('script');
                    s2.src="//"+self.srvModel.disqusShortname+".disqus.com/count.js";
                    s2.async= true;
                    s.setAttribute('data-timestamp', +new Date());
                    (d.head || d.body).appendChild(s);
                })();
            }
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

