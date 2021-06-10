Vue.component("coinswitch" , 
    {
        props: ["toCurrency", "toCurrencyDue", "toCurrencyAddress", "merchantId", "autoload", "mode"],
        data: function () {
            return {
                opened: false
            };
        },
        computed: {
            showInlineIFrame: function () {
                return this.url && this.opened;
            },
            url: function () {
                return window.location.origin + "/checkout/coinswitch.html?" +
                    "&toCurrency=" +
                    this.toCurrency +
                    "&toCurrencyAddress=" +
                    this.toCurrencyAddress +
                    "&toCurrencyDue=" +
                    this.toCurrencyDue +
                    "&mode=" +
                    this.mode +
                    (this.merchantId ? "&merchant_id=" + this.merchantId : "");
            }
        },
        methods: {
            openDialog: function (e) {
                if (e && e.preventDefault) {
                    e.preventDefault();
                }

                if (this.mode === 'inline') {
                    this.opened = true;

                } else if (this.mode === "popup") {
                    var coinSwitchWindow = window.open(
                        this.url,
                        'CoinSwitch',
                        'width=360,height=650,toolbar=0,menubar=0,location=0,status=1,scrollbars=1,resizable=0,left=0,top=0');
                    coinSwitchWindow.opener = null;
                    coinSwitchWindow.focus();
                }
            },
            closeDialog: function () {
                if (this.mode === 'inline') {
                    this.opened = false;
                }
            },
            onLoadIframe: function (event) {
                $("#prettydropdown-DefaultLang").hide();
                var c = this.closeDialog.bind(this);
                event.currentTarget.contentWindow.addEventListener("message", function (evt) {
                    if (evt && evt.data == "popup-closed") {
                        c();

                        $("#prettydropdown-DefaultLang").show();
                    }
                });
            }
        },
        mounted: function () {
            if (this.autoload) {
                this.openDialog();
            }
        }
    });
