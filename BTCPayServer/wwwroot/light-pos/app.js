var app = null;

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


    app = new Vue({
        el: '#app',
        data: function(){
            return {
                srvModel: window.srvModel,
                payTotal: '0',
                payTotalNumeric: 0,
                fontSize: 80,
                keys: ['1', '2', '3', '4', '5', '6', '7', '8', '9', '.', '0', 'C']
            }
        },
        computed: {
            Currency: function(){
                return this.srvModel.Currency.toUpperCase();
            },
        },
        watch: {
            payTotal: function(val) {
                var self = this;

                // This must be timeouted because the updated width is not available yet
                this.$nextTick(function(){
                    var displayWidth = self.getWidth(self.$refs.display),
                        amountWidth = self.getWidth(self.$refs.amount);

                    if (displayWidth <= amountWidth) {
                        var gamma = displayWidth / amountWidth || 0;
                        self.fontSize = Math.floor(self.fontSize * gamma);
                    }
                });
            }
        },
        methods: {
            getWidth: function(el) {
                var styles = window.getComputedStyle(el),
                    width = parseFloat(el.clientWidth),
                    padL = parseFloat(styles.paddingLeft),
                    padR = parseFloat(styles.paddingRight);

                return width - padL - padR;
            },
            clearTotal: function() {
                this.payTotal = '0';
                this.payTotalNumeric = 0;
            },
            buttonClicked: function(key) {
                
                var payTotal = this.payTotal;

                if (key === 'C') {
                    payTotal = payTotal.substring(0, payTotal.length - 1);
                    payTotal = payTotal === '' ? '0' : payTotal;
                } else if (key === '.') {
                    // Only add decimal point if it doesn't exist yet
                    if (payTotal.indexOf('.') === -1) {
                        payTotal += key;
                    }
                } else { // Is a digit
                    if (payTotal === '0') {
                        payTotal = '';
                    }
                    payTotal += key;

                    var divsibility = this.srvModel.currencyInfo.divisibility;
                    var decimalIndex = payTotal.indexOf('.')
                    if (decimalIndex !== -1 && (payTotal.length - decimalIndex-1  > divsibility)) {
                        payTotal= payTotal.replace(".","");
                        payTotal =  payTotal.substr(0,payTotal.length - divsibility ) + "." + payTotal.substr(payTotal.length - divsibility);
                    }
                }

                this.payTotal = payTotal;
                this.payTotalNumeric = parseFloat(payTotal);
            }
        }
    });
});

