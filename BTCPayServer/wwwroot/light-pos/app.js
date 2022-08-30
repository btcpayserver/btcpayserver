let app = null;

document.addEventListener("DOMContentLoaded",function () {
    const displayFontSize = 80;
    app = new Vue({
        el: '#app',
        data: function () {
            return {
                srvModel: window.srvModel,
                payTotal: '0',
                payTotalNumeric: 0,
                tipTotal: null,
                tipTotalNumeric: 0,
                discountPercent: null,
                discountTotalNumeric: 0,
                fontSize: displayFontSize,
                defaultFontSize: displayFontSize,
                keys: ['1', '2', '3', '4', '5', '6', '7', '8', '9', '.', '0', 'C'],
                payButtonLoading: false,
            }
        },
        created: function() {
            /** We need to unset state in case user clicks the browser back button */
            window.addEventListener('pagehide', this.unsetPayButtonLoading);
        },
        destroyed: function() {
            window.removeEventListener('pagehide', this.unsetPayButtonLoading);
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
                    const displayWidth = this.getWidth(this.$refs.display),
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
                const styles = window.getComputedStyle(el),
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
                this.discountPercent = null;
                this.discountTotalNumeric = 0;
            },
            handleFormSubmit: function() {
                this.payButtonLoading = true;
            },
            unsetPayButtonLoading: function() {
                this.payButtonLoading = false;
            },
            buttonClicked: function(key) {
                let payTotal = this.payTotal;

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

                    const { divisibility } = this.srvModel.currencyInfo;
                    const decimalIndex = payTotal.indexOf('.')
                    if (decimalIndex !== -1 && (payTotal.length - decimalIndex - 1  > divisibility)) {
                        payTotal = payTotal.replace(".", "");
                        payTotal = payTotal.substr(0, payTotal.length - divisibility) + "." + 
                            payTotal.substr(payTotal.length - divisibility);
                    }
                }

                this.payTotal = payTotal;
                this.payTotalNumeric = parseFloat(payTotal);
                this.tipTotalNumeric = 0;
                this.tipTotal = null;
                this.discountTotalNumeric = 0;
                this.discountPercent = null;
            },
            tipClicked: function(percentage) {
                const { divisibility } = this.srvModel.currencyInfo;
                this.payTotalNumeric -= this.tipTotalNumeric;
                this.tipTotalNumeric = parseFloat((this.payTotalNumeric * (percentage / 100)).toFixed(divisibility));
                this.payTotalNumeric = parseFloat((this.payTotalNumeric + this.tipTotalNumeric).toFixed(divisibility));
                this.payTotal = this.payTotalNumeric.toString(10);
                this.tipTotal = this.tipTotalNumeric === 0 ? null : this.tipTotalNumeric.toFixed(divisibility);
            },
            removeTip: function() {
                this.payTotalNumeric -= this.tipTotalNumeric;
                this.payTotal = this.payTotalNumeric.toString(10);
                this.tipTotalNumeric = 0;
                this.tipTotal = null;
            },
            removeDiscount: function() {
                this.payTotalNumeric += this.discountTotalNumeric;
                this.payTotal = this.payTotalNumeric.toString(10);
                this.discountTotalNumeric = 0;
                this.discountPercent = null;

                // Remove the tips as well as it won't be the right number anymore after discount is removed
                this.removeTip();
            },
            onDiscountChange: function (e){
                // Remove tip if we are changing discount % as it won't be the right number anymore
                this.removeTip();

                const discountPercent = parseFloat(e.target.value);
                const { divisibility } = this.srvModel.currencyInfo;

                this.payTotalNumeric += this.discountTotalNumeric;
                this.discountTotalNumeric = parseFloat((this.payTotalNumeric * (discountPercent / 100)).toFixed(divisibility));
                this.payTotalNumeric = parseFloat((this.payTotalNumeric - this.discountTotalNumeric).toFixed(divisibility));
                this.payTotal = this.payTotalNumeric.toString(10);
            },
        }
    });
});

