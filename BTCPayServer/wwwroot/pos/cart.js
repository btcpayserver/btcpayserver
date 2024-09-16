document.addEventListener("DOMContentLoaded",function () {
    new Vue({
        el: '#PosCart',
        mixins: [posCommon],
        data () {
            return {
                displayCategory: '*',
                searchTerm: null,
                cart: loadState('cart'),
                categoriesScrollable: false,
                $cart: null,
                amount: 0,
                persistState: true
            }
        },
        computed: {
            cartCount() {
                return this.cart.reduce((res, item) => res + (parseInt(item.count) || 0), 0)
            },
            amountNumeric () {
                return parseFloat(this.cart.reduce((res, item) => res + (item.price||0) * item.count, 0)
                    .toFixed(this.divisibility))
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
            cart: {
                handler(newCart) {
                    if (!newCart || newCart.length === 0) {
                        this.$cart.hide()
                    }
                }
            }
        },
        methods: {
            toggleCart() {
                this.$cart.toggle()
            }
        },
        mounted() {
            this.$cart = new bootstrap.Offcanvas(this.$refs.cart, { backdrop: false })
        }
    });
});
