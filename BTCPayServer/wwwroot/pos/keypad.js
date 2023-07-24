document.addEventListener("DOMContentLoaded",function () {
    const displayFontSize = 64;
    new Vue({
        el: '#PosKeypad',
        mixins: [posCommon],
        data () {
            return {
                mode: 'amount',
                fontSize: displayFontSize,
                defaultFontSize: displayFontSize,
                keys: ['1', '2', '3', '4', '5', '6', '7', '8', '9', '.', '0', 'del']
            }
        },
        computed: {
            modes () {
                const modes = [{ title: 'Amount', type: 'amount' }]
                if (this.showDiscount) modes.push({ title: 'Discount', type: 'discount' })
                if (this.enableTips) modes.push({ title: 'Tip', type: 'tip'})
                return modes
            },
            keypadTarget () {
                switch (this.mode) {
                    case 'amount':
                        return 'amount';
                    case 'discount':
                        return 'discountPercent';
                    case 'tip':
                        return 'tip';
                }
            }
        },
        watch: {
            total () {
                // This must be timed out because the updated width is not available yet
                this.$nextTick(function () {
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
            getWidth (el) {
                const styles = window.getComputedStyle(el),
                    width = parseFloat(el.clientWidth),
                    padL = parseFloat(styles.paddingLeft),
                    padR = parseFloat(styles.paddingRight);
                return width - padL - padR;
            },
            clear () {
                this.amount = this.tip = this.discount = this.tipPercent = this.discountPercent = null;
                this.mode = 'amount';
            },
            applyKeyToValue (key, value) {
                if (!value) value = '';
                if (key === 'del') {
                    value = value.substring(0, value.length - 1);
                    value = value === '' ? '0' : value;
                } else if (key === '.') {
                    // Only add decimal point if it doesn't exist yet
                    if (value.indexOf('.') === -1) {
                        value += key;
                    }
                } else { // Is a digit
                    if (!value || value === '0') {
                        value = '';
                    }
                    value += key;
                    const { divisibility } = this.currencyInfo;
                    const decimalIndex = value.indexOf('.')
                    if (decimalIndex !== -1 && (value.length - decimalIndex - 1  > divisibility)) {
                        value = value.replace('.', '');
                        value = value.substr(0, value.length - divisibility) + '.' +
                            value.substr(value.length - divisibility);
                    }
                }
                return value;
            },
            keyPressed (key) {
                this[this.keypadTarget] = this.applyKeyToValue(key, this[this.keypadTarget]);
            }
        },
        created() {
            /** We need to unset state in case user clicks the browser back button */
            window.addEventListener('pagehide', this.unsetPayButtonLoading)
        },
        destroyed() {
            window.removeEventListener('pagehide', this.unsetPayButtonLoading)
        },
    });
});

