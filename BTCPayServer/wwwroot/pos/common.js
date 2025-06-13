const POS_ITEM_ADDED_CLASS = 'posItem--added';

class PoSOrder {
    constructor(decimals) {
        this._decimals = decimals;
        this._discount = 0;
        this._tip = 0;
        this._tipPercent = 0;
        this.itemLines = [];
    }

    static ItemLine = class {
        constructor(itemId, count, unitPrice, taxRate = null) {
            this.itemId = itemId;
            this.count = count;
            this.unitPrice = unitPrice;
            this.taxRate = taxRate;
        }
    }

    addLine(line) {
        this.itemLines.push(line);
    }

    setTip(tip) {
        this._tip = this._round(tip);
        this._tipPercent = 0;
    }
    setTipPercent(tip) {
        this._tipPercent = tip;
        this._tip = 0;
    }

    addDiscountRate(discount) {
        this._discount = discount;
    }

    setCart(cart, amounts, defaultTaxRate) {
        this.itemLines = [];
        for (const item of cart) {
            this.addLine(new PoSOrder.ItemLine(item.id, item.count, item.price, item.taxRate ?? defaultTaxRate));
        }
        if (amounts) {
            var i = 1;
            for (const item of amounts) {
                if (!item) continue;
                this.addLine(new PoSOrder.ItemLine("Custom Amount " + i, 1, item, defaultTaxRate));
                i++;
            }
        }
    }

    // Returns the tax rate of the items in the cart.
    // If the tax rates are not all the same, returns null.
    // If the cart is empty, returns null.
    // Else, returns the tax rate shared by all items
    getTaxRate() {
        if (this.itemLines.length === 0) return null;
        var rate = this.itemLines[0].taxRate ?? 0;
        for (const line of this.itemLines.slice(1)) {
            if (rate !== line.taxRate)
            {
                return null;
            }
        }
        return rate;
    }
    calculate() {
        const ctx = {
            discount: 0,
            tax: 0,
            itemsTotal: 0,
            priceTaxExcluded: 0,
            tip: 0,
            priceTaxIncluded: 0,
            priceTaxIncludedWithTips: 0
        };

        for (const item of this.itemLines) {
            let linePrice = item.unitPrice * item.count;
            let discount = linePrice * this._discount / 100;
            discount = this._round(discount);
            ctx.discount += discount;
            linePrice -= discount;

            let taxRate = item.taxRate ?? 0;
            let tax = linePrice * taxRate / 100;
            tax = this._round(tax);
            ctx.tax += tax;
            ctx.priceTaxExcluded += linePrice;
        }

        ctx.priceTaxExcluded = this._round(ctx.priceTaxExcluded);
        ctx.tip = this._round(this._tip);
        ctx.tip += this._round(ctx.priceTaxExcluded * this._tipPercent / 100);
        ctx.priceTaxIncluded = ctx.priceTaxExcluded + ctx.tax;
        ctx.priceTaxIncludedWithTips = ctx.priceTaxIncluded + ctx.tip;
        ctx.priceTaxIncludedWithTips = this._round(ctx.priceTaxIncludedWithTips);
        ctx.itemsTotal = ctx.priceTaxExcluded + ctx.discount;

        return ctx;
    }

    _round(value) {
        const factor = Math.pow(10, this._decimals);
        return Math.round(value * factor + Number.EPSILON) / factor;
    }
}

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
            posOrder: new PoSOrder(srvModel.currencyInfo.divisibility),
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
        summary() {
            return this.posOrder.calculate();
        },
        itemsTotalNumeric() {
            // We don't want to show the items total if there is no discount or tip
            if (this.summary.itemsTotal === this.summary.priceTaxExcluded) return 0;
            return this.summary.itemsTotal;
        },
        taxNumeric() {
            return this.summary.tax;
        },
        taxPercent() {
            return this.posOrder.getTaxRate();
        },
        subtotalNumeric () {
            // We don't want to show the subtotal if there is no tax or tips
            if (this.summary.priceTaxExcluded === this.summary.priceTaxIncludedWithTips) return 0;
            return this.summary.priceTaxExcluded;
        },
        posdata () {
            const data = { subTotal: this.summary.priceTaxExcluded, total: this.summary.priceTaxIncludedWithTips }
            const amounts = this.amounts.filter(e => e) // clear empty or zero values
            if (amounts) data.amounts = amounts.map(parseFloat)
            if (this.cart) data.cart = this.cart
            if (this.summary.discount > 0) data.discountAmount = this.summary.discount
            if (this.discountPercentNumeric > 0) data.discountPercentage = this.discountPercentNumeric
            if (this.summary.tip > 0) data.tip = this.summary.tip
            if (this.tipPercent > 0) data.tipPercentage = this.tipPercent
            return JSON.stringify(data)
        },
        discountPercentNumeric () {
            const value = parseFloat(this.discountPercent)
            return isNaN(value) ? 0.0 : parseFloat(value.toFixed(this.currencyInfo.divisibility))
        },
        discountNumeric () {
            return this.summary.discount;
        },
        tipNumeric () {
            return this.summary.tip;
        },
        lastAmount() {
          return this.amounts[this.amounts.length - 1] = this.amounts[this.amounts.length - 1] || 0;
        },
        totalNumeric () {
            return this.summary.priceTaxIncludedWithTips;
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
            this.posOrder.addDiscountRate(isNaN(value) ? null : value)
        },
        tip(val) {
            this.tipPercent = null
            this.posOrder.setTip(val)
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
                this.posOrder.setCart(newCart, this.amounts, this.defaultTaxRate)
            },
            deep: true
        },
        amounts (values) {
            this.posOrder.setCart(this.cart, values, this.defaultTaxRate)
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
            this.posOrder.setTipPercent(this.tipPercent)
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
                    ...item,
                    count
                }
                this.cart.push(itemInCart);
            } else {
                itemInCart.count += count;
            }

            // Animate
            if (!$posItem.classList.contains(POS_ITEM_ADDED_CLASS)) $posItem.classList.add(POS_ITEM_ADDED_CLASS);

            this.posOrder.setCart(this.cart, this.amounts, this.defaultTaxRate);
            return itemInCart;
        },
        removeFromCart(id) {
            const index = this.cart.findIndex(lineItem => lineItem.id === id);
            this.cart.splice(index, 1);
            this.posOrder.setCart(this.cart, this.amounts, this.defaultTaxRate);
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
            this.posOrder.setCart(this.cart, this.amounts, this.defaultTaxRate);
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
            if (this.$refs.posItems) {
                const $displayed = this.$refs.posItems.querySelectorAll('.posItem.posItem--displayed')
                if ($displayed.length > 0) {
                    $displayed[0].classList.add('posItem--first')
                    $displayed[$displayed.length - 1].classList.add('posItem--last')
                }
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
        this.posOrder.setCart(this.cart, this.amounts, this.defaultTaxRate);
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
