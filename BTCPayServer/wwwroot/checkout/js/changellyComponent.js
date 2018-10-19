var ChangellyComponent =
    {
        props: ["storeId", "toCurrency", "toCurrencyDue", "toCurrencyAddress", "merchantId"],
        data: function () {
            return {
                currencies: [],
                isLoading: true,
                calculatedAmount: 0,
                selectedFromCurrency: "",
                prettyDropdownInstance: null,
                calculateError: false,
                currenciesError: false
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
                this.currenciesError = false;
                $.ajax(
                    {
                        context: this,
                        url: this.getUrl() + "/currencies",
                        dataType: "json",
                        success: function (result) {

                                for (i = 0; i < result.length; i++) {
                                    if (result[i].enabled &&
                                        result[i].name.toLowerCase() !== this.toCurrency.toLowerCase()) {
                                        this.currencies.push(result[i]);
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
                            
                        },
                        error: function(){
                            this.currenciesError = true;
                        },
                        complete: function () {
                            this.isLoading = false;
                        }
                    });
            },
            calculateAmount: function () {
                this.isLoading = true;
                this.calculateError = false;
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
                        error: function(){
                            this.calculateError = true;
                        },
                        complete: function () {
                            this.isLoading = false;
                        }
                    });
            },
            retry: function(type){
                if(type=="loadCurrencies"){
                    this.loadCurrencies();
                }else if(type=="calculateAmount"){
                    this.calculateAmount();
                }
            },
            onCurrencyChange: function (value) {
                this.selectedFromCurrency = value;
                this.calculatedAmount = 0;
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
