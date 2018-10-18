var ChangellyComponent =
    {
        props: ["storeId", "toCurrency", "toCurrencyDue", "toCurrencyAddress", "merchantId"],
        data: function () {
            return {
                currencies: [],
                isLoading: true,
                calculatedAmount: 0,
                selectedFromCurrency: "",
                prettyDropdownInstance: null
            };
        },
        computed: {
            url: function () {
                if (this.calculatedAmount && this.selectedFromCurrency && !this.isLoading) {
                    return "https://changelly.com/widget/v1?auth=email" +
                        "&from=" +
                        this.selectedFromCurrency +
                        "&to=" +
                        this.toCurrency +
                        "&address=" +
                        this.toCurrencyAddress +
                        "&amount=" +
                        this.calculatedAmount +
                        (this.merchantId ? "&merchant_id=" + this.merchantId + "&ref_id=" + this.merchantId : "");
                }
                return null;
            }
        },
        watch: {
            selectedFromCurrency: function (val) {
                if (val) {
                    this.calculateAmount();
                } else {
                    this.calculateAmount = 0;
                }
            }
        },
        mounted: function () {
            this.prettyDropdownInstance = initDropdown(this.$refs.changellyCurrenciesDropdown);
            this.loadCurrencies();
        },
        methods: {
            getUrl: function () {
                return window.location.origin + "/changelly/" + this.storeId;
            },
            loadCurrencies: function () {
                this.isLoading = true;
                $.ajax(
                    {
                        context: this,
                        url: this.getUrl() + "/currencies",
                        dataType: "json",
                        success: function (result) {
                            if (result.item2) {
                                for (i = 0; i < result.item1.length; i++) {
                                    if (result.item1[i].enabled &&
                                        result.item1[i].name.toLowerCase() !== this.toCurrency.toLowerCase()) {
                                        this.currencies.push(result.item1[i]);
                                    }
                                }
                                var self = this;
                                Vue.nextTick(function () {
                                    self.prettyDropdownInstance
                                        .refresh()
                                        .on("change",
                                            function (event) {
                                                self.onCurrencyChange(self.$refs.changellyCurrenciesDropdown.value);
                                            });
                                });
                            }
                        },
                        complete: function () {
                            this.isLoading = false;
                        }
                    });
            },
            calculateAmount: function () {
                this.isLoading = true;
                $.ajax(
                    {
                        url: this.getUrl() + "/calculate",
                        dataType: "json",
                        data: {
                            fromCurrency: this.selectedFromCurrency,
                            toCurrency: this.toCurrency,
                            toCurrencyAmount: this.toCurrencyDue
                        },
                        context: this,
                        success: function (result) {
                            this.calculatedAmount = result;
                        },
                        complete: function () {
                            this.isLoading = false;
                        }
                    });


            },
            onCurrencyChange: function (value) {
                this.selectedFromCurrency = value;
            },
            openDialog: function (e) {
                if (e && e.preventDefault) {
                    e.preventDefault();
                }

                var changellyWindow = window.open(
                    this.url,
                    'Changelly',
                    'width=600,height=470,toolbar=0,menubar=0,location=0,status=1,scrollbars=1,resizable=0,left=0,top=0');
                changellyWindow.focus();
            }
        }
    };
