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
                $cart: null
            }
        },
        computed: {
            cartCount() {
                return this.cart.reduce((res, item) => res + (parseInt(item.count) || 0), 0)
            },
            amountNumeric () {
                return parseFloat(this.cart.reduce((res, item) => res + item.price * item.count, 0).toFixed(this.currencyInfo.divisibility))
            },
            posdata () {
                const data = {
                    cart: this.cart,
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
            searchTerm(term) {
                const t = term.toLowerCase();
                this.forEachItem(item => {
                    const terms = item.dataset.search.toLowerCase()
                    const included = terms.indexOf(t) !== -1
                    item.classList[included ? 'remove' : 'add']("d-none")
                })
            },
            displayCategory(category) {
                this.forEachItem(item => {
                    const categories = JSON.parse(item.dataset.categories)
                    const included = category === "*" || categories.includes(category)
                    item.classList[included ? 'remove' : 'add']("d-none")
                })
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
                let itemInCart = this.cart.find(lineItem => lineItem.id === item.id);

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
                const $posItem = this.$refs.posItems.querySelectorAll('.posItem')[index];
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
            }
        },
        mounted() {
            this.$cart = new bootstrap.Offcanvas(this.$refs.cart, {backdrop: false})
            window.addEventListener('pagehide', () => {
                if (this.payButtonLoading) {
                    this.unsetPayButtonLoading();
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
        },
    });
});
