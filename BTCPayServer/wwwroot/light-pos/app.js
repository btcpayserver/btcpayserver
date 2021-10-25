var app = null;


document.addEventListener("DOMContentLoaded",function (ev) {
    app = new Vue({
        el: '#app',
        data: function(){
            var displayFontSize = 80;

            return {
                srvModel: window.srvModel,
                payTotal: '0',
                payTotalNumeric: 0,
                tipTotal: null,
                tipTotalNumeric: 0,
                fontSize: displayFontSize,
                defaultFontSize: displayFontSize,
                keys: ['1', '2', '3', '4', '5', '6', '7', '8', '9', '.', '0', 'C']
            }
        },
        computed: {
            Currency: function(){
                return this.srvModel.Currency.toUpperCase();
            },
        },
        watch: {
            payTotal: function() {
                // This must be timeouted because the updated width is not available yet
                this.$nextTick(function(){
                    var displayWidth = this.getWidth(this.$refs.display),
                        amountWidth = this.getWidth(this.$refs.amount),
                        gamma = displayWidth / amountWidth || 0,
                        isAmountWider = displayWidth < amountWidth;

                    if (isAmountWider) {
                        // Font size will get smaller
                        this.fontSize = Math.floor(this.fontSize * gamma);
                    } else if (!isAmountWider && this.fontSize < this.defaultFontSize) {
                        // Font size will get larger up to the max size
                        this.fontSize = Math.min(this.fontSize * gamma, this.defaultFontSize);
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
                this.tipTotal = null;
                this.tipTotalNumeric = 0;
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

                    var divisibility = this.srvModel.currencyInfo.divisibility;
                    var decimalIndex = payTotal.indexOf('.')
                    if (decimalIndex !== -1 && (payTotal.length - decimalIndex - 1  > divisibility)) {
                        payTotal = payTotal.replace(".", "");
                        payTotal = payTotal.substr(0, payTotal.length - divisibility) + "." + payTotal.substr(payTotal.length - divisibility);
                    }
                }

                this.payTotal = payTotal;
                this.payTotalNumeric = parseFloat(payTotal);
                this.tipTotalNumeric = 0;
                this.tipTotal = null;
            },
            tipClicked: function(percentage) {
                this.payTotalNumeric -= this.tipTotalNumeric;
                this.tipTotalNumeric = parseFloat((this.payTotalNumeric * (percentage / 100)).toFixed(this.srvModel.currencyInfo.divisibility));
                this.payTotalNumeric = parseFloat((this.payTotalNumeric + this.tipTotalNumeric).toFixed(this.srvModel.currencyInfo.divisibility));

                this.payTotal = this.payTotalNumeric.toString(10);
                this.tipTotal = this.tipTotalNumeric === 0 ? null : this.tipTotalNumeric.toFixed(this.srvModel.currencyInfo.divisibility);
            },
            removeTip: function() {
                this.payTotalNumeric -= this.tipTotalNumeric;
                this.payTotal = this.payTotalNumeric.toString(10);
                this.tipTotalNumeric = 0;
                this.tipTotal = null;
            },
        }
    });
});

