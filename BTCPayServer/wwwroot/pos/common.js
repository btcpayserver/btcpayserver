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
    computed: {
        amountNumeric () {
            const value = parseFloat(this.amount)
            return isNaN(value) ? 0.0 : parseFloat(value.toFixed(this.currencyInfo.divisibility))
        },
        discountPercentNumeric () {
            const value = parseFloat(this.discountPercent)
            return isNaN(value) ? 0.0 : parseFloat(value.toFixed(this.currencyInfo.divisibility))
        },
        discountNumeric () {
            return this.amountNumeric && this.discountPercentNumeric
                ? parseFloat((this.amountNumeric * (this.discountPercentNumeric / 100)).toFixed(this.currencyInfo.divisibility))
                : 0.0;
        },
        amountMinusDiscountNumeric () {
            return parseFloat((this.amountNumeric - this.discountNumeric).toFixed(this.currencyInfo.divisibility))
        },
        tipNumeric () {
            if (this.tipPercent) {
                return parseFloat((this.amountMinusDiscountNumeric * (this.tipPercent / 100)).toFixed(this.currencyInfo.divisibility))
            } else {
                if (this.tip < 0) {
                    this.tip = 0
                }
                const value = parseFloat(this.tip)
                return isNaN(value) ? 0.0 : parseFloat(value.toFixed(this.currencyInfo.divisibility))
            }
        },
        total () {
            return this.amountNumeric - this.discountNumeric + this.tipNumeric
        },
        totalNumeric () {
            return parseFloat(parseFloat(this.total).toFixed(this.currencyInfo.divisibility))
        },
        posdata () {
            const data = {
                subTotal: this.amountNumeric,
                total: this.totalNumeric
            }
            if (this.tipNumeric > 0) data.tip = this.tipNumeric
            if (this.tipPercent > 0) data.tipPercentage = this.tipPercent
            if (this.discountNumeric > 0) data.discountAmount = this.discountNumeric
            if (this.discountPercentNumeric > 0) data.discountPercentage = this.discountPercentNumeric
            return JSON.stringify(data)
        }
    },
    watch: {
        discountPercent (val) {
            const value = parseFloat(val)
            if (isNaN(value)) this.discountPercent = null
            else if (value < 0) this.discountPercent = '0'
            else if (value > 100) this.discountPercent = '100'
            else this.discountPercent = value.toString()
        },
        tip (val) {
            this.tipPercent = null
        }
    },
    methods: {
        handleFormSubmit() {
            this.payButtonLoading = true;
        },
        getLocale(currency) {
            switch (currency) {
                case 'USD': return 'en-US'
                case 'EUR': return 'de-DE'
                case 'JPY': return 'ja-JP'
                default: return navigator.language
            }
        },
        tipPercentage (percentage) {
            this.tipPercent = this.tipPercent !== percentage
                ? percentage
                : null;
        },
        formatCrypto (value, withSymbol) {
            const symbol = withSymbol ? ` ${this.currencySymbol || this.currencyCode}` : ''
            const { divisibility } = this.currencyInfo
            return parseFloat(value).toFixed(divisibility) + symbol
        },
        formatCurrency (value, withSymbol) {
            const currency = this.currencyCode
            if (currency === 'BTC' || currency === 'SATS') return this.formatCrypto(value, withSymbol)
            const { divisibility } = this.currencyInfo
            const locale = this.getLocale(currency);
            const style = withSymbol ? 'currency' : 'decimal'
            const opts = { currency, style, maximumFractionDigits: divisibility, minimumFractionDigits: divisibility }
            try {
                return new Intl.NumberFormat(locale, opts).format(value)
            } catch (err) {
                return this.formatCrypto(value, withSymbol)
            }
        },
    }
}
