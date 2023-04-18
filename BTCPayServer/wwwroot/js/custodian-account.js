new Vue({
    el: '#custodianAccountView',
    components: {
        qrcode: VueQrcode
    },
    data: {
        account: null,
        hideDustAmounts: true,
        modals: {
            trade: null,
            withdraw: null,
            deposit: null
        },
        deposit: {
            asset: null,
            paymentMethod: null,
            address: null,
            link: null,
            errorMsg: null,
            cryptoImageUrl: null,
            tab: null,
            isLoading: false
        },
        trade: {
            row: null,
            results: null,
            errorMsg: null,
            isExecuting: false,
            isUpdating: false,
            simulationAbortController: null,
            priceRefresherInterval: null,
            fromAsset: null,
            toAsset: null,
            qty: null,
            maxQty: null,
            price: null,
            priceForPair: {}
        },
        withdraw: {
            asset: null,
            paymentMethod: null,
            errorMsg: null,
            qty: null,
            minQty: null,
            maxQty: null,
            badConfigFields: null,
            results: null,
            isUpdating: null,
            isExecuting: false,
            simulationAbortController: null,
            ledgerEntries: null
        },
    },
    computed: {
        tradeQtyToReceive: function () {
            return this.trade.qty / this.trade.price;
        },
        canExecuteTrade: function () {
            return this.trade.qty >= this.getMinQtyToTrade() && this.trade.price !== null && this.trade.fromAsset !== null && this.trade.toAsset !== null && !this.trade.isExecuting && this.trade.results === null;
        },
        availableAssetsToTrade: function () {
            let r = [];
            let balances = this?.account?.assetBalances;
            if (balances) {
                let t = this;
                let rows = Object.values(balances);
                rows = rows.filter(function (row) {
                    return row.fiatValue > t.account.dustThresholdInFiat;
                });

                for (let i in rows) {
                    r.push(rows[i].asset);
                }
            }
            return r.sort();
        },
        availableAssetsToTradeInto: function () {
            let r = [];
            let pairs = this.account?.assetBalances?.[this.trade.fromAsset]?.tradableAssetPairs;
            if (pairs) {
                for (let i in pairs) {
                    let pair = pairs[i];
                    if (pair.assetBought === this.trade.fromAsset) {
                        r.push(pair.assetSold);
                    } else if (pair.assetSold === this.trade.fromAsset) {
                        r.push(pair.assetBought);
                    }
                }
            }
            return r.sort();
        },
        availableAssetsToDeposit: function () {
            let paymentMethods = this?.account?.depositablePaymentMethods;
            let r = [];
            if (paymentMethods && paymentMethods.length > 0) {
                for (let i = 0; i < paymentMethods.length; i++) {
                    let asset = paymentMethods[i].split("-")[0];
                    if (r.indexOf(asset) === -1) {
                        r.push(asset);
                    }
                }
            }
            return r.sort();
        },
        availablePaymentMethodsToDeposit: function () {
            let paymentMethods = this?.account?.depositablePaymentMethods;
            let r = [];
            if (Array.isArray(paymentMethods)) {
                for (let i = 0; i < paymentMethods.length; i++) {
                    let pm = paymentMethods[i];
                    let asset = pm.split("-")[0];
                    if (asset === this.deposit.asset) {
                        r.push(pm);
                    }
                }
            }
            return r.sort();
        },
        sortedAssetRows: function () {
            if (this.account?.assetBalances) {
                let rows = Object.values(this.account.assetBalances);
                let t = this;

                if (this.hideDustAmounts) {
                    rows = rows.filter(function (row) {
                        return row.fiatValue === null || row.fiatValue > t.account.dustThresholdInFiat;
                    });
                }

                rows = rows.sort(function (a, b) {
                    if(b.fiatValue !== null && a.fiatValue !== null){
                        return b.fiatValue - a.fiatValue;
                    }else if(b.fiatValue !== null && a.fiatValue === null){
                        return 1;
                    }else if(b.fiatValue === null && a.fiatValue !== null) {
                        return -1;
                    }else{
                        return b.asset.localeCompare(a.asset);
                    }
                });

                return rows;
            }
        },
        canExecuteWithdrawal: function () {
            return (this.withdraw.minQty != null && this.withdraw.qty >= this.withdraw.minQty)
                && (this.withdraw.maxQty != null && this.withdraw.qty <= this.withdraw.maxQty)
                && this.withdraw.badConfigFields?.length === 0
                && this.withdraw.paymentMethod
                && !this.withdraw.isExecuting
                && !this.withdraw.isUpdating
                && this.withdraw.results === null;
        },
        availableAssetsToWithdraw: function () {
            let r = [];
            const balances = this?.account?.assetBalances;
            if (balances) {
                for (let asset in balances) {
                    const balance = balances[asset];
                    if (balance?.withdrawablePaymentMethods?.length) {
                        r.push(asset);
                    }
                }
            }
            ;
            return r.sort();
        },
        availablePaymentMethodsToWithdraw: function () {
            if (this.withdraw.asset) {
                let paymentMethods = this?.account?.assetBalances?.[this.withdraw.asset]?.withdrawablePaymentMethods;
                if (paymentMethods) {
                    return paymentMethods.sort();
                }
            }
            return [];
        },
        withdrawFees: function(){
            let r = [];
            if(this.withdraw.ledgerEntries){
                for (let i = 0; i< this.withdraw.ledgerEntries.length; i++){
                    let entry = this.withdraw.ledgerEntries[i];
                    if(entry.type === 'Fee'){
                        r.push(entry);
                    }
                }
            }
            return r;
        }
    },
    methods: {
        getMaxQty: function (fromAsset) {
            let row = this.account?.assetBalances?.[fromAsset];
            if (row) {
                return row.qty;
            }
            return null;
        },
        getMinQtyToTrade: function (fromAsset = this.trade.fromAsset, toAsset = this.trade.toAsset) {
            if (fromAsset && toAsset && this.account?.assetBalances) {
                for (let asset in this.account.assetBalances) {
                    let row = this.account.assetBalances[asset];

                    let pairCode = fromAsset + "/" + toAsset;
                    let pairCodeReverse = toAsset + "/" + fromAsset;

                    let pair = row.tradableAssetPairs?.[pairCode];
                    let pairReverse = row.tradableAssetPairs?.[pairCodeReverse];

                    if (pair !== null || pairReverse !== null) {
                        if (pair && !pairReverse) {
                            return pair.minimumTradeQty;
                        } else if (!pair && pairReverse) {
                            let price = this.trade.priceForPair?.[pairCode];
                            if (!price) {
                                return null;
                            }
                            // if (reverse) {
                            //     return price / pairReverse.minimumTradeQty;
                            // }else {
                            return price * pairReverse.minimumTradeQty;
                            // }
                        }
                    }
                }
            }
            return 0;
        },
        setTradeQtyPercent: function (percent) {
            this.trade.qty = percent / 100 * this.trade.maxQty;
        },
        setWithdrawQtyPercent: function (percent) {
            this.withdraw.qty = percent / 100 * this.withdraw.maxQty;
        },
        openTradeModal: function (row) {
            let _this = this;
            this.trade.row = row;
            this.trade.results = null;
            this.trade.errorMsg = null;
            this.trade.fromAsset = row.asset;
            if (row.asset === this.account.storeDefaultFiat) {
                this.trade.toAsset = "BTC";
            } else {
                this.trade.toAsset = this.account.storeDefaultFiat;
            }

            this.trade.qty = row.qty;
            this.trade.maxQty = row.qty;
            this.trade.price = row.bid;

            if (this.modals.trade === null) {
                this.modals.trade = new window.bootstrap.Modal('#tradeModal');

                // Disable price refreshing when modal closes...
                const tradeModelElement = document.getElementById('tradeModal')
                tradeModelElement.addEventListener('hide.bs.modal', event => {
                    _this.setTradePriceRefresher(false);
                });
            }

            this.setTradePriceRefresher(true);
            this.modals.trade.show();
        },
        openWithdrawModal: function (row) {
            this.withdraw.asset = row.asset;
            this.withdraw.qty = row.qty;
            this.withdraw.paymentMethod = null;
            this.withdraw.minQty = 0;
            this.withdraw.maxQty = row.qty;
            this.withdraw.results = null;
            this.withdraw.errorMsg = null;
            this.withdraw.isUpdating = null;
            this.withdraw.isExecuting = false;

            if (this.modals.withdraw === null) {
                this.modals.withdraw = new window.bootstrap.Modal('#withdrawModal');
            }

            this.modals.withdraw.show();
        },
        openDepositModal: function (row) {
            if (this.modals.deposit === null) {
                this.modals.deposit = new window.bootstrap.Modal('#depositModal');
            }
            if (row) {
                this.deposit.asset = row.asset;
            } else if (!this.deposit.asset && this.availableAssetsToDeposit.length > 0) {
                this.deposit.asset = this.availableAssetsToDeposit[0];
            }

            this.modals.deposit.show();
        },
        onTradeSubmit: async function (e) {
            e.preventDefault();

            const form = e.currentTarget;
            const url = form.getAttribute('action');
            const method = form.getAttribute('method');

            this.trade.isExecuting = true;

            // Prevent the modal from closing by clicking outside or via the keyboard
            this.setModalCanBeClosed(this.modals.trade, false);

            const _this = this;
            const response = await fetch(url, {
                method: method,
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': this.getRequestVerificationToken()
                },
                body: JSON.stringify({
                    fromAsset: _this.trade.fromAsset,
                    toAsset: _this.trade.toAsset,
                    qty: _this.trade.qty
                })
            });

            let data = null;
            try {
                data = await response.json();
            } catch (e) {
            }

            if (response.ok) {
                _this.trade.results = data;
                _this.trade.errorMsg = null;

                _this.setTradePriceRefresher(false);
                _this.refreshAccountBalances();
            } else {
                _this.trade.errorMsg = data && data.message || "Error";
            }
            _this.setModalCanBeClosed(_this.modals.trade, true);
            _this.trade.isExecuting = false;
        },

        onWithdrawSubmit: async function (e) {
            e.preventDefault();

            const form = e.currentTarget;
            const url = form.getAttribute('action');
            const method = form.getAttribute('method');

            this.withdraw.isExecuting = true;
            this.setModalCanBeClosed(this.modals.withdraw, false);
            
            let dataToSubmit = {
                paymentMethod: this.withdraw.paymentMethod,
                qty: this.withdraw.qty
            };

            const _this = this;
            const response = await fetch(url, {
                method: method,
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': this.getRequestVerificationToken()
                },
                body: JSON.stringify(dataToSubmit)
            });

            let data = null;
            try {
                data = await response.json();
            } catch (e) {
            }

            if (response.ok) {
                _this.withdraw.results = data;
                _this.withdraw.errorMsg = null;

                _this.refreshAccountBalances();
            } else {
                _this.withdraw.errorMsg = data && data.message || "Error";
            }
            _this.setModalCanBeClosed(_this.modals.withdraw, true);
            _this.withdraw.isExecuting = false;
        },

        setTradePriceRefresher: function (enabled) {
            if (enabled) {
                // Update immediately...
                this.updateTradePrice();

                // And keep updating every few seconds...
                let _this = this;
                this.trade.priceRefresherInterval = setInterval(function () {
                    _this.updateTradePrice();
                }, 5000);

            } else {
                clearInterval(this.trade.priceRefresherInterval);
            }
        },

        updateTradePrice: function () {
            if (!this.trade.fromAsset || !this.trade.toAsset) {
                // We need to know the 2 assets or we cannot do anything...
                return;
            }

            if (this.trade.fromAsset === this.trade.toAsset) {
                // The 2 assets must be different
                this.trade.price = null;
                return;
            }

            if (this.trade.isUpdating) {
                // Previous request is still running. No need to hammer the server
                return;
            }

            this.trade.isUpdating = true;

            let dataToSubmit = {
                fromAsset: this.trade.fromAsset,
                toAsset: this.trade.toAsset,
                qty: this.trade.qty
            };
            let url = window.ajaxTradeSimulateUrl;

            this.trade.simulationAbortController = new AbortController();

            let _this = this;
            fetch(url, {
                    method: "POST",
                    body: JSON.stringify(dataToSubmit),
                    signal: this.trade.simulationAbortController.signal,
                    headers: {
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': this.getRequestVerificationToken()
                    }
                }
            ).then(function (response) {
                    _this.trade.isUpdating = false;

                    if (response.ok) {
                        return response.json();
                    }
                    // _this.trade.results = data;
                    // _this.trade.errorMsg = null; }
                    // Do nothing on error
                }
            ).then(function (data) {
                _this.trade.maxQty = data.maxQty;

                // By default trade everything
                if (_this.trade.qty === null) {
                    _this.trade.qty = _this.trade.maxQty;
                }

                // Cannot trade more than what we have
                if (data.maxQty < _this.trade.qty) {
                    _this.trade.qty = _this.trade.maxQty;
                }
                let pair = data.toAsset + "/" + data.fromAsset;
                let pairReverse = data.fromAsset + "/" + data.toAsset;

                // TODO Should we use "bid" in some cases? The spread can be huge with some shitcoins.
                _this.trade.price = data.ask;
                _this.trade.priceForPair[pair] = data.ask;
                _this.trade.priceForPair[pairReverse] = 1 / data.ask;

            }).catch(function (e) {
                _this.trade.isUpdating = false;
                if (e instanceof DOMException && e.code === DOMException.ABORT_ERR) {
                    // User aborted fetch request
                } else {
                    throw e;
                }
            });
        },
        canDepositAsset: function (asset) {
            let paymentMethods = this?.account?.depositablePaymentMethods;
            if (paymentMethods && paymentMethods.length > 0) {
                for (let i = 0; i < paymentMethods.length; i++) {
                    let pmParts = paymentMethods[i].split("-");
                    if (asset === pmParts[0]) {
                        return true;
                    }
                }
            }
            return false;
        },
        canSwapTradeAssets: function () {
            let minQtyToTrade = this.getMinQtyToTrade(this.trade.toAsset, this.trade.fromAsset);
            let assetToTradeIntoHoldings = this.account?.assetBalances?.[this.trade.toAsset];
            if (assetToTradeIntoHoldings) {
                return assetToTradeIntoHoldings.qty >= minQtyToTrade;
            }
        },
        swapTradeAssets: function () {
            // Swap the 2 assets
            let tmp = this.trade.fromAsset;
            this.trade.fromAsset = this.trade.toAsset;
            this.trade.toAsset = tmp;
            this.trade.price = 1 / this.trade.price;

            this.refreshTradeSimulation();
        },
        refreshTradeSimulation: function () {
            let maxQty = this.getMaxQty(this.trade.fromAsset);
            this.trade.qty = maxQty
            this.trade.maxQty = maxQty;

            if(this.trade.simulationAbortController) {
                this.trade.simulationAbortController.abort();
            }

            // Update the price asap, so we can continue
            let _this = this;
            setTimeout(function () {
                _this.updateTradePrice();
            }, 100);
        },
        refreshAccountBalances: function () {
            let _this = this;
            fetch(window.ajaxBalanceUrl).then(function (response) {
                return response.json();
            }).then(function (result) {
                _this.account = result;
                
                for(let asset in _this.account.assetBalances){
                    let assetInfo = _this.account.assetBalances[asset];
                    
                    if(asset !== _this.account.storeDefaultFiat) {
                        let pair1 = asset + '/' + _this.account.storeDefaultFiat;
                        _this.trade.priceForPair[pair1] = assetInfo.bid;

                        let pair2 = _this.account.storeDefaultFiat + '/' + asset;
                        _this.trade.priceForPair[pair2] = 1 / assetInfo.bid;
                    }
                }
                
            });
        },
        getRequestVerificationToken: function () {
            return document.querySelector("input[name='__RequestVerificationToken']").value;
        },
        setModalCanBeClosed: function (modal, flag) {
            modal._config.keyboard = flag;
            if (flag) {
                modal._config.backdrop = true;
            } else {
                modal._config.backdrop = 'static';
            }
        },
        refreshWithdrawalSimulation: function () {
            if(!this.withdraw.paymentMethod || !this.withdraw.qty){
                // We are missing required data, stop now.
                return;
            }
            
            if(this.withdraw.simulationAbortController) {
                this.withdraw.simulationAbortController.abort();
            }

            let data = {
                paymentMethod: this.withdraw.paymentMethod,
                qty: this.withdraw.qty
            };
            const _this = this;
            const token = this.getRequestVerificationToken();

            this.withdraw.isUpdating = true;
            this.withdraw.simulationAbortController = new AbortController();
            fetch(window.ajaxWithdrawSimulateUrl, {
                method: "POST",
                body: JSON.stringify(data),
                signal: this.withdraw.simulationAbortController.signal,
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token
                }
            }).then(function (response) {
                _this.withdraw.isUpdating = false;
                return response.json();
            }).then(function (data) {
                if (data.minQty === null) {
                    _this.withdraw.minQty = 0;
                } else {
                    _this.withdraw.minQty = data.minQty;
                }
                if (data.maxQty === null) {
                    _this.withdraw.maxQty = _this.account.assetBalances?.[_this.withdraw.asset]?.qty;
                } else {
                    _this.withdraw.maxQty = data.maxQty;
                }

                if (_this.withdraw.qty === null || _this.withdraw.qty > _this.withdraw.maxQty) {
                    _this.withdraw.qty = _this.withdraw.maxQty;
                }
                _this.withdraw.badConfigFields = data.badConfigFields;
                _this.withdraw.errorMsg = data.errorMessage;
                _this.withdraw.ledgerEntries = data.ledgerEntries;
            });
        },
        getStoreDefaultFiatValueForAsset: function(asset){
            // TODO 
        }
    },
    watch: {
        'trade.fromAsset': function (newValue, oldValue) {
            if (newValue === this.trade.toAsset) {
                // This is the same as swapping the 2 assets
                this.trade.toAsset = oldValue;
                this.trade.price = 1 / this.trade.price;

                this.refreshTradeSimulation();
            }
            if (newValue !== oldValue) {
                // The qty is going to be wrong, so set to 100%
                this.trade.qty = this.getMaxQty(this.trade.fromAsset);
            }
        },
        'deposit.asset': function (newValue, oldValue) {
            if (this.availablePaymentMethodsToDeposit.length > 0) {
                this.deposit.paymentMethod = this.availablePaymentMethodsToDeposit[0];
            } else {
                this.deposit.paymentMethod = null;
            }
        },
        'deposit.paymentMethod': function (newValue, oldValue) {
            let _this = this;
            const token = this.getRequestVerificationToken();
            this.deposit.isLoading = true;
            fetch(window.ajaxDepositUrl + "?paymentMethod=" + encodeURI(this.deposit.paymentMethod), {
                method: "GET",
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token
                }
            }).then(function (response) {
                _this.deposit.isLoading = false;
                return response.json();
            }).then(function (data) {
                _this.deposit.address = data.address;
                _this.deposit.link = data.link;
                _this.deposit.createTransactionUrl = data.createTransactionUrl;
                _this.deposit.cryptoImageUrl = data.cryptoImageUrl;

                if (!_this.deposit.tab) {
                    _this.deposit.tab = 'address';
                }
                if (_this.deposit.tab === 'address' && !_this.deposit.address && _this.deposit.link) {
                    // Tab "address" is not available, but tab "link" is.
                    _this.deposit.tab = 'link';
                }

                _this.deposit.errorMsg = data.errorMessage;
            });
        },
        'withdraw.asset': function (newValue, oldValue) {
            if (this.availablePaymentMethodsToWithdraw.length > 0) {
                this.withdraw.paymentMethod = this.availablePaymentMethodsToWithdraw[0];
            } else {
                this.withdraw.paymentMethod = null;
            }
        },
        'withdraw.paymentMethod': function (newValue, oldValue) {
            if (this.withdraw.paymentMethod && this.withdraw.qty) {
                this.withdraw.minQty = 0;
                this.withdraw.maxQty = null;
                this.withdraw.errorMsg = null;
                this.withdraw.badConfigFields = null;

                this.refreshWithdrawalSimulation();
            }
        },
        'withdraw.qty': function (newValue, oldValue) {
            if (newValue > this.withdraw.maxQty) {
                this.withdraw.qty = this.withdraw.maxQty;
            }
            this.refreshWithdrawalSimulation();
        }
    },
    created: function () {
        this.refreshAccountBalances();
    },
    mounted: function () {
        // Runs when the app is ready
    }
});
