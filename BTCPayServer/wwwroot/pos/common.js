const posCommon = {
    data () {
        return {
            ...srvModel,
            amount: null,
            tip: null,
            tipPercent: null,
            discount: null,
            discountPercent: null,
            payButtonLoading: false
        }
    },
    created() {
        /** We need to unset state in case user clicks the browser back button */
        window.addEventListener('pagehide', this.unsetPayButtonLoading);
    },
    destroyed() {
        window.removeEventListener('pagehide', this.unsetPayButtonLoading);
    },
    computed: {
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
        }
    },
    methods: {
        handleFormSubmit() {
            this.payButtonLoading = true;
        },
        getLocale(currency) {
            switch (currency) {
                case 'USD': return 'en-US';
                case 'EUR': return 'de-DE';
                case 'JPY': return 'ja-JP';
                default: return navigator.language;
            }
        },
        tipPercentage (percentage) {
            this.tipPercent = this.tipPercent !== percentage
                ? percentage
                : null;
        },
        unsetPayButtonLoading () {
            this.payButtonLoading = false;
        },
        formatCrypto (value, withSymbol) {
            const symbol = withSymbol ? ` ${this.currencySymbol || this.currencyCode}` : '';
            const divisibility = this.currencyInfo.divisibility;
            return parseFloat(value).toFixed(divisibility) + symbol;
        },
        formatCurrency (value, withSymbol) {
            const currency = this.currencyCode;
            if (currency === 'BTC' || currency === 'SATS') return this.formatCrypto(value, withSymbol);
            const divisibility = this.currencyInfo.divisibility;
            const locale = this.getLocale(currency);
            const style = withSymbol ? 'currency' : 'decimal';
            const opts = { currency, style, maximumFractionDigits: divisibility, minimumFractionDigits: divisibility };
            try {
                return new Intl.NumberFormat(locale, opts).format(value);
            } catch (err) {
                return this.formatCrypto(value, withSymbol);
            }
        },
    }
}
