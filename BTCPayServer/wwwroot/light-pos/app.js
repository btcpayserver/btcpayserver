document.addEventListener("DOMContentLoaded",function () {
    const displayFontSize = 64;
    new Vue({
        el: '#app',
        data () {
            return {
                srvModel: window.srvModel,
                mode: 'amount',
                amount: null,
                tip: null,
                tipPercent: null,
                discount: null,
                discountPercent: null,
                fontSize: displayFontSize,
                defaultFontSize: displayFontSize,
                keys: ['1', '2', '3', '4', '5', '6', '7', '8', '9', '.', '0', 'del'],
                payButtonLoading: false
            }
        },
        computed: {
            modes () {
                const modes = [{ title: 'Amount', type: 'amount' }]
                if (this.srvModel.showDiscount) modes.push({ title: 'Discount', type: 'discount' })
                if (this.srvModel.enableTips) modes.push({ title: 'Tip', type: 'tip'})
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
            },
            calculation () {
                if (!this.tipNumeric && !this.discountNumeric) return null
                let calc = this.formatCurrency(this.amountNumeric, true)
                if (this.discountNumeric > 0) calc += ` - ${this.formatCurrency(this.discountNumeric, true)} (${this.discountPercent}%)`
                if (this.tipNumeric > 0) calc += ` + ${this.formatCurrency(this.tipNumeric, true)}`
                if (this.tipPercent) calc += ` (${this.tipPercent}%)`
                return calc
            },
            amountNumeric () {
                const value = parseFloat(this.amount)
                return isNaN(value) ? 0.0 : value
            },
            discountPercentNumeric () {
                const value = parseFloat(this.discountPercent)
                return isNaN(value) ? 0.0 : value;
            },
            discountNumeric () {
                return this.amountNumeric && this.discountPercentNumeric
                    ? this.amountNumeric * (this.discountPercentNumeric / 100)
                    : 0.0;
            },
            amountMinusDiscountNumeric () {
                return this.amountNumeric - this.discountNumeric;
            },
            tipNumeric () {
                if (this.tipPercent) {
                    return this.amountMinusDiscountNumeric * (this.tipPercent / 100);
                } else {
                    const value = parseFloat(this.tip)
                    return isNaN(value) ? 0.0 : value;
                }
            },
            total () {
                return (this.amountNumeric - this.discountNumeric + this.tipNumeric);
            },
            totalNumeric () {
                return parseFloat(this.total);
            },
            posdata () {
                const data = {
                    subTotal: this.amountNumeric,
                    total: this.totalNumeric
                }
                if (this.tipNumeric > 0) data.tip = this.tipNumeric
                if (this.discountNumeric > 0) data.discountAmount = this.discountNumeric
                if (this.discountPercentNumeric > 0) data.discountPercentage = this.discountPercentNumeric
                return JSON.stringify(data)
            }
        },
        watch: {
            discountPercent (val) {
                const value = parseFloat(val)
                if (isNaN(value)) this.discountPercent = null
                else if (value > 100) this.discountPercent = '100'
                else this.discountPercent = value.toString();
            },
            tip (val) {
                this.tipPercent = null;
            },
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
            handleFormSubmit () {
                this.payButtonLoading = true;
            },
            unsetPayButtonLoading () {
                this.payButtonLoading = false;
            },
            formatCrypto (value, withSymbol) {
                const symbol = withSymbol ? ` ${this.srvModel.currencySymbol || this.srvModel.currencyCode}` : '';
                const divisibility = this.srvModel.currencyInfo.divisibility;
                return parseFloat(value).toFixed(divisibility) + symbol;
            },
            formatCurrency (value, withSymbol) {
                const currency = this.srvModel.currencyCode;
                if (currency === 'BTC' || currency === 'SATS') return this.formatCrypto(value, withSymbol); 
                const divisibility = this.srvModel.currencyInfo.divisibility;
                const locale = this.getLocale(currency);
                const style = withSymbol ? 'currency' : 'decimal';
                const opts = { currency, style, maximumFractionDigits: divisibility, minimumFractionDigits: divisibility };
                try {
                    return new Intl.NumberFormat(locale, opts).format(value);
                } catch (err) {
                    return this.formatCrypto(value, withSymbol);
                }
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
                    const { divisibility } = this.srvModel.currencyInfo;
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
            },
            tipPercentage (percentage) {
                this.tipPercent = this.tipPercent !== percentage
                    ? percentage
                    : null;
            },
            getLocale(currency) {
                switch (currency) {
                    case 'USD': return 'en-US';
                    case 'EUR': return 'de-DE';
                    case 'JPY': return 'ja-JP';
                    default: return navigator.language;
                }
            }
        },
        created () {
            /** We need to unset state in case user clicks the browser back button */
            window.addEventListener('pagehide', this.unsetPayButtonLoading);
        },
        destroyed () {
            window.removeEventListener('pagehide', this.unsetPayButtonLoading);
        }
    });
});

