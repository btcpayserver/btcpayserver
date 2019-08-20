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

    Vue.component('contribute', {
        props: ["targetCurrency", "active", "perks", "inModal", "displayPerksRanking", "loading"],
        template: "#contribute-template"
    });

    Vue.component('perks', {
        props: ["perks", "targetCurrency", "active", "inModal","displayPerksRanking", "loading"],
        template: "#perks-template"
    });

    Vue.component('perk', {
        props: ["perk", "targetCurrency", "active", "inModal", "displayPerksRanking", "index", "loading"],
        template: "#perk-template",
        data: function () {
            return {
                amount: null,
                expanded: false
            }
        },
        computed: {
            canExpand: function(){
                return !this.expanded && this.active && (this.perk.price.value || this.perk.custom) && (this.perk.inventory==null || this.perk.inventory > 0)
            }
        },
        methods: {
            onContributeFormSubmit: function (e) {
                if (e) {
                    e.preventDefault();
                }
                if(!this.active || this.loading){
                    return;
                }
                
                eventAggregator.$emit("contribute", {amount: parseFloat(this.amount), choiceKey: this.perk.id});
            },
            expand: function(){
                if(this.canExpand){
                    this.expanded = true;
                }
            },
            setAmount: function (amount) {
                this.amount = (amount || 0).noExponents();
                this.expanded = false;
            }


        },
        mounted: function () {
            this.setAmount(this.perk.price.value);
        },
        watch: {
            perk: function (newValue, oldValue) {
                if (newValue.price.value != oldValue.price.value) {
                    this.setAmount(newValue.price.value);
                }
            }
        }
        
    });
    
    app = new Vue({
        el: '#app',
        data: function(){
            return {
                srvModel: window.srvModel,
                connectionStatus: "",
                endDate: "",
                startDate: "",
                started: false,
                ended: false,
                contributeModalOpen: false,
                endDiff: "",
                startDiff: "",
                active: true,
                animation: true, 
                sound: true,
                lastUpdated:"",
                loading: false,
                timeoutState: 0
            }
        },
        computed: {
            raisedAmount: function(){
               return parseFloat(this.srvModel.info.currentAmount + this.srvModel.info.currentPendingAmount ).toFixed(this.srvModel.currencyData.divisibility) ;
            },
            percentageRaisedAmount: function(){
                return parseFloat(this.srvModel.info.progressPercentage + this.srvModel.info.pendingProgressPercentage ).toFixed(2);
            },
            targetCurrency: function(){
                return this.srvModel.targetCurrency.toUpperCase();
            },
            paymentStats: function(){
                var result= [];
                
                var combinedStats = {};
                
                
                var keys = Object.keys(this.srvModel.info.paymentStats);

                for (var i = 0; i < keys.length; i++) {
                    if(combinedStats[keys[i]]){
                        combinedStats[keys[i]] +=this.srvModel.info.paymentStats[keys[i]];
                    }else{
                        combinedStats[keys[i]] =this.srvModel.info.paymentStats[keys[i]];
                    }
                }

                keys = Object.keys(this.srvModel.info.pendingPaymentStats);
                
                for (var i = 0; i < keys.length; i++) {
                    if(combinedStats[keys[i]]){
                        combinedStats[keys[i]] +=this.srvModel.info.pendingPaymentStats[keys[i]];
                    }else{
                        combinedStats[keys[i]] =this.srvModel.info.pendingPaymentStats[keys[i]];
                    }
                }

                keys = Object.keys(combinedStats);

                for (var i = 0; i < keys.length; i++) {
                    var newItem = {key:keys[i], value: combinedStats[keys[i]], label: keys[i].replace("_","")};
                    result.push(newItem);
                    
                }
                for (var i = 0; i < result.length; i++) {
                    var current = result[i];
                    if(current.label.endsWith("LightningLike")){
                        current.label = current.label.substr(0,current.label.indexOf("LightningLike"));
                        current.lightning = true;
                    }
                }
                    
                return result;
            },
            perks: function(){
                var result = [];
                for (var i = 0; i < this.srvModel.perks.length; i++) {
                    var currentPerk = this.srvModel.perks[i];
                    if(this.srvModel.perkCount.hasOwnProperty(currentPerk.id)){
                        currentPerk.sold = this.srvModel.perkCount[currentPerk.id];
                    }
                    result.push(currentPerk);
                }
                return result;
            }
        },
        methods: {
            updateComputed: function () {
                if (this.srvModel.endDate) {
                    var endDateM = moment(this.srvModel.endDate);
                    this.endDate = endDateM.format('MMMM Do YYYY');
                    this.ended = endDateM.isBefore(moment());
                    
                }else{
                    this.ended = false;
                    this.endDate = null;
                }

                if (this.srvModel.startDate) {
                    var startDateM = moment(this.srvModel.startDate);
                    this.startDate = startDateM.format('MMMM Do YYYY');
                    this.started = startDateM.isBefore(moment());
                }else{
                    this.started = true;
                    this.startDate = null;
                }
                if(this.started && !this.ended && this.srvModel.endDate){
                    var mDiffD =  moment(this.srvModel.endDate).diff(moment(), "days");
                    var mDiffH =  moment(this.srvModel.endDate).diff(moment(), "hours");
                    var mDiffM =  moment(this.srvModel.endDate).diff(moment(), "minutes");
                    var mDiffS =  moment(this.srvModel.endDate).diff(moment(), "seconds");
                    this.endDiff =  mDiffD > 0? mDiffD + " days" : mDiffH> 0? mDiffH + " hours" : mDiffM> 0? mDiffM+ " minutes" : mDiffS> 0? mDiffS + " seconds": "";
                }else{
                    this.endDiff = null;
                }
                if(!this.started && this.srvModel.startDate){
                    var mDiffD =  moment(this.srvModel.startDate).diff(moment(), "days");
                    var mDiffH =  moment(this.srvModel.startDate).diff(moment(), "hours");
                    var mDiffM =  moment(this.srvModel.startDate).diff(moment(), "minutes");
                    var mDiffS =  moment(this.srvModel.startDate).diff(moment(), "seconds");
                    this.startDiff =  mDiffD > 0? mDiffD + " days" : mDiffH> 0? mDiffH + " hours" : mDiffM> 0? mDiffM+ " minutes" : mDiffS> 0? mDiffS + " seconds": "";
                }else {
                    this.startDiff = null;
                }
                this.lastUpdated = moment(this.srvModel.info.lastUpdated).calendar();
                this.active = this.started && !this.ended;
                setTimeout(this.updateComputed, 1000);
            },
            setLoading: function(val){
                this.loading = val;
                if(this.timeoutState){
                    clearTimeout(this.timeoutState);
                }
            }
        },
        mounted: function () {
            hubListener.connect();
            var self = this;
            this.sound = this.srvModel.soundsEnabled;
            this.animation = this.srvModel.animationsEnabled;
            eventAggregator.$on("invoice-created", function (invoiceId) {
                btcpay.showInvoice(invoiceId);
                btcpay.showFrame();

                self.contributeModalOpen = false;
                self.setLoading(false);
            });

            eventAggregator.$on("contribute", function () {
                self.setLoading(true);
                
                self.timeoutState = setTimeout(function(){
                    self.setLoading(false);
                },5000);
            });
            eventAggregator.$on("invoice-error", function(error){

                self.setLoading(false);
                var msg = "";
                if(typeof error === "string"){
                    msg = error;
                }else if(!error){
                    msg = "Unknown Error";
                }else{
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
                } );
            });
            eventAggregator.$on("payment-received", function (amount, cryptoCode, type) {
                var onChain = type.toLowerCase() === "btclike";
                if(self.sound) {
                    playRandomSound();
                }
                if(self.animation) {
                    fireworks();
                }
                amount = parseFloat(amount).noExponents();
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

