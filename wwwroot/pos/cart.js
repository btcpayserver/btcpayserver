document.addEventListener("DOMContentLoaded",function () {
    new Vue({
        el: '#PosCart',
        mixins: [posCommon],
        data () {
            return {
                $cart: null,
                amount: 0,
                persistState: true
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
