const POS_ITEM_ADDED_CLASS = 'posItem--added';

function storageKey(name) {
    return `${srvModel.appId}-${srvModel.currencyCode}-${name}`;
}
function saveState(name, data) {
    localStorage.setItem(storageKey(name), JSON.stringify(data));
}
function loadState(name) {
    const data = localStorage.getItem(storageKey(name))
    if (!data) return []
    const cart = JSON.parse(data);

    for (let i = cart.length-1; i >= 0; i--) {
        if (!cart[i]) {
            cart.splice(i, 1);
            continue;
        }
        //check if the pos items still has the cached cart items
        const matchedItem = srvModel.items.find(item => item.id === cart[i].id);
        if (!matchedItem){
            cart.splice(i, 1);
        } else {
            if (matchedItem.inventory != null && matchedItem.inventory <= 0){
                //item is out of stock
                cart.splice(i, 1);
            } else if (matchedItem.inventory != null && matchedItem.inventory < cart[i].count){
                //not enough stock for original cart amount, reduce to available stock
                cart[i].count = matchedItem.inventory;
                //update its stock
                cart[i].inventory = matchedItem.inventory;
            }
        }
    }
    return cart;
}

const posCommon = {
    data () {
        return {
            ...srvModel,
            amount: null,
            tip: null,
            tipPercent: null,
            discount: null,
            discountPercent: null,
            payButtonLoading: false,
            categoriesScrollable: false,
            displayCategory: '*',
            searchTerm: null,
            cart: [],
            amounts: [null],
            recentTransactions: [],
            recentTransactionsLoading: false,
            dateFormatter: new Intl.DateTimeFormat('default', { dateStyle: 'short', timeStyle: 'short' }),
        }
    },
    computed: {
        amountNumeric () {
            const { divisibility } = this.currencyInfo
            const cart = this.cart.reduce((res, item) => res + (item.price || 0) * item.count, 0).toFixed(divisibility)
            const value = parseFloat(this.amount || 0) + parseFloat(cart)
            return isNaN(value) ? 0.0 : parseFloat(value.toFixed(divisibility))
        },
        posdata () {
            const data = { subTotal: this.amountNumeric, total: this.totalNumeric }
            const amounts = this.amounts.filter(e => e) // clear empty or zero values
            if (amounts) data.amounts = amounts.map(parseFloat)
            if (this.cart) data.cart = this.cart
            if (this.discountNumeric > 0) data.discountAmount = this.discountNumeric
            if (this.discountPercentNumeric > 0) data.discountPercentage = this.discountPercentNumeric
            if (this.tipNumeric > 0) data.tip = this.tipNumeric
            if (this.tipPercent > 0) data.tipPercentage = this.tipPercent
            return JSON.stringify(data)
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
        cartCount() {
            return this.cart.reduce((res, item) => res + (parseInt(item.count) || 0), 0)
        }
    },
    watch: {
        searchTerm(term) {
            this.updateDisplay()
        },
        displayCategory(category) {
            this.updateDisplay()
        },
        discountPercent(val) {
            const value = parseFloat(val)
            if (isNaN(value)) this.discountPercent = null
            else if (value < 0) this.discountPercent = '0'
            else if (value > 100) this.discountPercent = '100'
            else this.discountPercent = value.toString()
        },
        tip(val) {
            this.tipPercent = null
        },
        cart: {
            handler(newCart) {
                newCart.forEach(item => {
                    if (!item.count) item.count = 1
                    if (item.inventory && item.inventory < item.count) item.count = item.inventory
                })
                if (this.persistState) {
                    saveState('cart', newCart)
                }
            },
            deep: true
        },
        amounts (values) {
            this.amount = values.reduce((total, current) => total + parseFloat(current || '0'), 0);
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
        tipPercentage(percentage) {
            this.tipPercent = this.tipPercent !== percentage
                ? percentage
                : null;
        },
        formatCrypto(value, withSymbol) {
            const symbol = withSymbol ? ` ${this.currencySymbol || this.currencyCode}` : ''
            const { divisibility } = this.currencyInfo
            return parseFloat(value).toFixed(divisibility) + symbol
        },
        formatCurrency(value, withSymbol) {
            const currency = this.currencyCode
            if (currency === 'BTC' || currency === 'SATS') return this.formatCrypto(value, withSymbol)
            const { divisibility } = this.currencyInfo;
            const locale = this.getLocale(currency);
            const style = withSymbol ? 'currency' : 'decimal'
            const opts = { currency, style, maximumFractionDigits: divisibility, minimumFractionDigits: divisibility }
            try {
                return new Intl.NumberFormat(locale, opts).format(value)
            } catch (err) {
                return this.formatCrypto(value, withSymbol)
            }
        },
        inStock(index) {
            const item = this.items[index]
            const itemInCart = this.cart.find(lineItem => lineItem.id === item.id)

            return item.inventory == null || item.inventory > (itemInCart ? itemInCart.count : 0)
        },
        inventoryText(index) {
            const item = this.items[index]
            if (item.inventory == null) return null

            const itemInCart = this.cart.find(lineItem => lineItem.id === item.id)
            const left = item.inventory - (itemInCart ? itemInCart.count : 0)
            return left > 0 ? `${item.inventory} left` : 'Sold out'
        },
        addToCart(index, count) {
            if (!this.inStock(index)) return null;

            const item = this.items[index];
            const $posItem = this.$refs.posItems.querySelectorAll('.posItem')[index];

            // Check if price is needed
            const isFixedPrice = item.priceType.toLowerCase() === 'fixed';
            if (!isFixedPrice) {
                const $amount = $posItem.querySelector('input[name="amount"]');
                if (!$amount.reportValidity()) return false;
                item.price = parseFloat($amount.value);
            }

            let itemInCart = this.cart.find(lineItem => lineItem.id === item.id && lineItem.price === item.price);

            // Add new item because it doesn't exist yet
            if (!itemInCart) {
                itemInCart = {
                    id: item.id,
                    title: item.title,
                    price: item.price,
                    inventory: item.inventory,
                    count
                }
                this.cart.push(itemInCart);
            } else {
                itemInCart.count += count;
            }

            // Animate
            if (!$posItem.classList.contains(POS_ITEM_ADDED_CLASS)) $posItem.classList.add(POS_ITEM_ADDED_CLASS);

            return itemInCart;
        },
        removeFromCart(id) {
            const index = this.cart.findIndex(lineItem => lineItem.id === id);
            this.cart.splice(index, 1);
        },
        getQuantity(id) {
            const itemInCart = this.cart.find(lineItem => lineItem.id === id);
            return itemInCart ? itemInCart.count : 0;
        },
        updateQuantity(id, count, addOrRemove) {
            let itemInCart = this.cart.find(lineItem => lineItem.id === id);
            if (!itemInCart && addOrRemove && count > 0) {
                const index = this.items.findIndex(lineItem => lineItem.id === id);
                itemInCart = this.addToCart(index, 0);
            }
            const applyable = addOrRemove || (count < 0 && itemInCart.count + count > 0) ||
                (count > 0 && (itemInCart.inventory == null || itemInCart.count + count <= itemInCart.inventory));
            if (applyable) {
                itemInCart.count += count;
            }
            if (itemInCart && itemInCart.count <= 0 && addOrRemove) {
                this.removeFromCart(itemInCart.id);
            }
        },
        clear() {
            this.cart = [];
            this.amounts = [null];
            this.tip = this.discount = this.tipPercent = this.discountPercent = null;
        },
        forEachItem(callback) {
            if (this.$refs.posItems) {
                this.$refs.posItems.querySelectorAll('.posItem').forEach(callback)
            }
        },
        displayItem(item) {
            const inSearch = !this.searchTerm ||
                decodeURIComponent(item.dataset.search ? item.dataset.search.toLowerCase() : '')
                    .indexOf(this.searchTerm.toLowerCase()) !== -1
            const inCategories = this.displayCategory === "*" ||
                (item.dataset.categories ? JSON.parse(item.dataset.categories) : [])
                    .includes(this.displayCategory)
            return inSearch && inCategories
        },
        updateDisplay() {
            this.forEachItem(item => {
                item.classList[this.displayItem(item) ? 'add' : 'remove']('posItem--displayed')
                item.classList.remove('posItem--first')
                item.classList.remove('posItem--last')
            })
            const $displayed = this.$refs.posItems.querySelectorAll('.posItem.posItem--displayed')
            if ($displayed.length > 0) {
                $displayed[0].classList.add('posItem--first')
                $displayed[$displayed.length - 1].classList.add('posItem--last')
            }
        },
        hideRecentTransactions() {
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
    beforeMount() {
        if (this.persistState) {
            this.cart = loadState('cart');
        }
    },
    mounted () {
        if (this.$refs.categories) {
            const getInnerNavWidth = () => {
                // set to inline display, get width to get the real inner width, then set back to flex
                this.$refs.categoriesNav.classList.remove('d-flex');
                this.$refs.categoriesNav.classList.add('d-inline-flex');
                const navWidth = this.$refs.categoriesNav.clientWidth - 32; // 32 is the margin
                this.$refs.categoriesNav.classList.remove('d-inline-flex');
                this.$refs.categoriesNav.classList.add('d-flex');
                return navWidth;
            }
            const adjustCategories = () => {
                const navWidth = getInnerNavWidth();
                Vue.set(this, 'categoriesScrollable', this.$refs.categories.clientWidth <= navWidth);
                const activeEl = document.querySelector('#Categories .btcpay-pills input:checked + label')
                if (activeEl) activeEl.scrollIntoView({ block: 'end', inline: 'center' })
            }
            window.addEventListener('resize', e => {
                debounce('resize', adjustCategories, 50)
            });
            adjustCategories();
        }
        
        this.forEachItem(item => {
            item.addEventListener('transitionend', () => {
                if (item.classList.contains(POS_ITEM_ADDED_CLASS)) {
                    item.classList.remove(POS_ITEM_ADDED_CLASS);
                }
            });
        })
        
        if (this.$refs.RecentTransactions) {
            this.$refs.RecentTransactions.addEventListener('show.bs.modal', this.loadRecentTransactions);
        }

        window.addEventListener('pagehide', () => {
            if (this.payButtonLoading) {
                this.payButtonLoading = false;
                localStorage.removeItem(storageKey('cart'));
            }
        })
        
        this.updateDisplay()
    }
}
