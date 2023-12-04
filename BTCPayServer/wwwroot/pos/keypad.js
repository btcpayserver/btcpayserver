document.addEventListener("DOMContentLoaded",function () {
    const displayFontSize = 64;
    new Vue({
        el: '#app',
        mixins: [posCommon],
        data () {
            return {
                mode: 'amounts',
                fontSize: displayFontSize,
                defaultFontSize: displayFontSize,
                keys: ['1', '2', '3', '4', '5', '6', '7', '8', '9', 'C', '0', '+'],
                amounts: [null],
                recentTransactions: [],
                recentTransactionsLoading: false,
                dateFormatter: new Intl.DateTimeFormat('default', { dateStyle: 'short', timeStyle: 'short' })
            }
        },
        computed: {
            modes () {
                const modes = [{ title: 'Amount', type: 'amounts' }]
                if (this.showDiscount) modes.push({ title: 'Discount', type: 'discount' })
                if (this.enableTips) modes.push({ title: 'Tip', type: 'tip'})
                return modes
            },
            keypadTarget () {
                switch (this.mode) {
                    case 'amounts':
                        return 'amounts';
                    case 'discount':
                        return 'discountPercent';
                    case 'tip':
                        return 'tip';
                }
            },
            calculation () {
                if (!this.tipNumeric && !(this.discountNumeric > 0 || this.discountPercentNumeric > 0) && this.amounts.length < 2) return null
                let calc = this.amounts.map(amt => this.formatCurrency(amt, true)).join(' + ')
                if (this.discountNumeric > 0 || this.discountPercentNumeric > 0) calc += ` - ${this.formatCurrency(this.discountNumeric, true)} (${this.discountPercent}%)`
                if (this.tipNumeric > 0) calc += ` + ${this.formatCurrency(this.tipNumeric, true)}`
                if (this.tipPercent) calc += ` (${this.tipPercent}%)`
                return calc
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
            },
            amounts (values) {
                this.amount = values.reduce((total, current) => total + parseFloat(current || '0'), 0);
            }
        },
        methods: {
            getWidth(el) {
                const styles = window.getComputedStyle(el),
                    width = parseFloat(el.clientWidth),
                    padL = parseFloat(styles.paddingLeft),
                    padR = parseFloat(styles.paddingRight);
                return width - padL - padR;
            },
            clear() {
                this.amounts = [null];
                this.tip = this.discount = this.tipPercent = this.discountPercent = null;
                this.mode = 'amounts';
            },
            applyKeyToValue(key, value, divisibility) {
                if (!value || value === '0') value = '';
                value = (value + key)
                    .replace('.', '')
                    .padStart(divisibility, '0')
                    .replace(new RegExp(`(\\d*)(\\d{${divisibility}})`), '$1.$2');
                return parseFloat(value).toFixed(divisibility);
            },
            keyPressed (key) {
                if (this.keypadTarget === 'amounts') {
                    const lastIndex = this.amounts.length - 1;
                    const lastAmount = this.amounts[lastIndex];
                    if (key === 'C') {
                        if (!lastAmount && lastIndex === 0) {
                            // clear completely
                            this.clear();
                        } else if (!lastAmount) {
                            // remove latest value
                            this.amounts.pop();
                        } else {
                            // clear latest value
                            Vue.set(this.amounts, lastIndex, null);
                        }
                    } else if (key === '+' && parseFloat(lastAmount || '0')) {
                        this.amounts.push(null);
                    } else { // Is a digit
                        const { divisibility } = this.currencyInfo;
                        const value = this.applyKeyToValue(key, lastAmount, divisibility);
                        Vue.set(this.amounts, lastIndex, value);
                    }
                } else {
                    if (key === 'C') {
                        this[this.keypadTarget] = null;
                    } else {
                        const divisibility = this.keypadTarget === 'tip' ? this.currencyInfo.divisibility : 0;
                        this[this.keypadTarget] = this.applyKeyToValue(key, this[this.keypadTarget], divisibility);
                    }
                }
            },
            doubleClick (key) {
                if (key === 'C') {
                    // clear completely
                    this.clear();
                }
            },
            closeModal() {
                bootstrap.Modal.getInstance(this.$refs.RecentTransactions).hide();
            },
            displayDate(val) {
                const date = new Date(val);
                return this.dateFormatter.format(date);
            },
            async loadRecentTransactions() {
                this.recentTransactionsLoading = true;
                const { url } = this.$refs.RecentTransactions.dataset;
                try {
                    const response = await fetch(url);
                    if (response.ok) {
                        this.recentTransactions = await response.json();
                    }
                } catch (error) {
                    console.error(error);
                } finally {
                    this.recentTransactionsLoading = false;
                }
            }
        },
        created() {
            // We need to unset state in case user clicks the browser back button
            window.addEventListener('pagehide', () => { this.payButtonLoading = false })
        },
        mounted() {
            this.$refs.RecentTransactions.addEventListener('show.bs.modal', this.loadRecentTransactions);
        }
    });
});

