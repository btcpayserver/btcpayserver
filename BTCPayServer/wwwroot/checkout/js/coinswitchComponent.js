var CoinSwitchComponent =
    {
        props: ["toCurrency", "toCurrencyDue", "toCurrencyAddress", "merchantId", "autoload"],
        data: function () {
        },
        computed: {
            url: function () {
                return window.location.origin + "/checkout/coinswitch.html?" +
                    "&toCurrency=" +
                    this.toCurrency +
                    "&toCurrencyAddress=" +
                    this.toCurrencyAddress +
                    "&toCurrencyDue=" +
                    this.toCurrencyDue +
                    (this.merchantId ? "&merchant_id=" + this.merchantId : "");
            }
        },
        methods: {
            openDialog: function (e) {
                if (e && e.preventDefault) {
                    e.preventDefault();
                }

                var coinSwitchWindow = window.open(
                    this.url,
                    'CoinSwitch',
                    'width=600,height=470,toolbar=0,menubar=0,location=0,status=1,scrollbars=1,resizable=0,left=0,top=0');
                coinSwitchWindow.opener = null;
                coinSwitchWindow.focus();
                
            }
        },
        mounted: function () {
            if(this.autoload){
                this.openDialog();
            }
        },
    };
