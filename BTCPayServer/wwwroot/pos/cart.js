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
                continue;
            } else {
                if (matchedItem.inventory != null && matchedItem.inventory <= 0){
                    //item is out of stock
                    cart.splice(i, 1);
                } else if (matchedItem.inventory != null && matchedItem.inventory < cart[i].count){
                    //not enough stock for original cart amount, reduce to available stock
                    cart[i].count = matchedItem.inventory;
                }
                //update its stock
                cart[i].inventory = matchedItem.inventory;
            }
            // Delete the disabled flag if any
            delete(cart[i].disabled);
        }
        return cart;
    }
    
    new Vue({
        el: '#app',
        mixins: [posCommon],
        data () {
            return {
                displayCategory: '*',
                searchTerm: null,
                cart: loadState('cart'),
            }
        },
        computed: {
            cartCount() {
                return this.cart.reduce((res, item) => res + item.count, 0);
            }
        },
        watch: {
            searchTerm(term) {
                const t = term.toLowerCase();
                this.forEachItem(item => {
                    const terms = item.dataset.search.toLowerCase();
                    const included = terms.indexOf(t) !== -1;
                    item.classList[included ? 'remove' : 'add']("d-none");
                })
            },
            displayCategory(category) {
                this.forEachItem(item => {
                    const categories = JSON.parse(item.dataset.categories);
                    const included = category === "*" || categories.includes(category);
                    item.classList[included ? 'remove' : 'add']("d-none");
                })
            }
        },
        methods: {
            forEachItem(callback) {
                this.$refs.posItems.querySelectorAll('.posItem').forEach(callback)
            },
            addToCart(index) {
                const item = this.items[index];
                let itemInCart = this.cart.find(lineItem => lineItem.id === item.id);

                // Add new item because it doesn't exist yet
                if (!itemInCart && (item.inventory == null || item.inventory <= 1)) {
                    itemInCart = { ...item, count: 0 }
                    this.cart.push(itemInCart);
                }
                
                // no inventory cases
                if (!itemInCart) return false;
                if (item.inventory != null && item.inventory <= itemInCart.count) return false;

                itemInCart.count += 1;
                saveState('cart', this.cart);
                return true;
            },
            removeFromCart(id) {
                const index = this.cart.findIndex(lineItem => lineItem.id === id);
                this.cart.splice(index, 1);
                saveState('cart', this.cart);
            }
        }
    });
});
