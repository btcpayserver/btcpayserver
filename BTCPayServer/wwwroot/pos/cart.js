document.addEventListener("DOMContentLoaded",function () {
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
    
    const POS_ITEM_ADDED_CLASS = 'posItem--added';
    
    new Vue({
        el: '#PosCart',
        mixins: [posCommon],
        data () {
            return {
                displayCategory: '*',
                searchTerm: null,
                cart: loadState('cart'),
                categoriesScrollable: false,
                $cart: null
            }
        },
        computed: {
            cartCount() {
                return this.cart.reduce((res, item) => res + (parseInt(item.count) || 0), 0)
            },
            amountNumeric () {
                return parseFloat(this.cart.reduce((res, item) => res + (item.price||0) * item.count, 0).toFixed(this.currencyInfo.divisibility))
            },
            posdata () {
                const data = { cart: this.cart, subTotal: this.amountNumeric }
                if (this.discountNumeric > 0) data.discountAmount = this.discountNumeric
                if (this.discountPercentNumeric > 0) data.discountPercentage = this.discountPercentNumeric
                if (this.tipNumeric > 0) data.tip = this.tipNumeric
                data.total = this.totalNumeric
                return JSON.stringify(data)
            }
        },
        watch: {
            searchTerm(term) {
                this.updateDisplay()
            },
            displayCategory(category) {
                this.updateDisplay()
            },
            cart: {
                handler(newCart) {
                    newCart.forEach(item => {
                        if (!item.count) item.count = 1
                        if (item.inventory && item.inventory < item.count) item.count = item.inventory
                    })
                    saveState('cart', newCart)
                    if (!newCart || newCart.length === 0) {
                        this.$cart.hide()
                    }
                },
                deep: true
            }
        },
        methods: {
            toggleCart() {
                this.$cart.toggle()
            },
            forEachItem(callback) {
                this.$refs.posItems.querySelectorAll('.posItem').forEach(callback)
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
            addToCart(index) {
                if (!this.inStock(index)) return false;
                
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
                        count: 0
                    }
                    this.cart.push(itemInCart);
                }

                itemInCart.count += 1;
                
                // Animate
                if(!$posItem.classList.contains(POS_ITEM_ADDED_CLASS)) $posItem.classList.add(POS_ITEM_ADDED_CLASS);
                
                return true;
            },
            removeFromCart(id) {
                const index = this.cart.findIndex(lineItem => lineItem.id === id);
                this.cart.splice(index, 1);
            },
            updateQuantity(id, count) {
                const itemInCart = this.cart.find(lineItem => lineItem.id === id);
                const applyable = (count < 0 && itemInCart.count + count > 0) ||
                    (count > 0 && (itemInCart.inventory == null || itemInCart.count + count <= itemInCart.inventory));
                if (applyable) {
                    itemInCart.count += count;
                }
            },
            clearCart() {
                this.cart = [];
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
            }
        },
        mounted() {
            this.$cart = new bootstrap.Offcanvas(this.$refs.cart, { backdrop: false })
            
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
                    Vue.set(this, 'categoriesScrollable', this.$refs.categories.clientWidth < navWidth);
                    const activeEl = document.querySelector('#Categories .btcpay-pills input:checked + label')
                    if (activeEl) activeEl.scrollIntoView({ block: 'end', inline: 'center' })
                }
                window.addEventListener('resize', e => {
                    debounce('resize', adjustCategories, 50)
                });
                adjustCategories();
            }

            window.addEventListener('pagehide', () => {
                if (this.payButtonLoading) {
                    this.payButtonLoading = false;
                    localStorage.removeItem(storageKey('cart'));
                }
            })
            this.forEachItem(item => {
                item.addEventListener('transitionend', () => {
                    if (item.classList.contains(POS_ITEM_ADDED_CLASS)) {
                        item.classList.remove(POS_ITEM_ADDED_CLASS);
                    }
                });
            })
            this.updateDisplay()
        },
    });
});
