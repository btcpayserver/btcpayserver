document.addEventListener("DOMContentLoaded",function () {
    new Vue({
        el: '#app',
        data () {
            return {
                payButtonLoading: false
            }
        },
        computed: {
        },
        methods: {
            handleFormSubmit () {
                this.payButtonLoading = true;
            },
            getLocale(currency) {
                switch (currency) {
                    case 'USD': return 'en-US';
                    case 'EUR': return 'de-DE';
                    case 'JPY': return 'ja-JP';
                    default: return navigator.language;
                }
            }
        }
    });
});
