new Vue({
    el: '#custodianAccountView',
    data: {
        account: null,
        hideDustAmounts: true,
        modals: {
            trade: null,
            withdraw: null,
            deposit: null
        },
        trade: {
            row: null,
            results: null,
            errorMsg: null,
            isExecuting: false,
            isUpdating: false,
            updateTradePriceAbortController: new AbortController(),
            priceRefresherInterval: null,
            assetToTrade: null,
            assetToTradeInto: null,
            qty: null,
            maxQtyToTrade: null,
            price: null,
            priceForPair: {}
        }
    },
    computed: {
        tradeQtyToReceive: function () {
            return this.trade.qty / this.trade.price;
        },
        canExecuteTrade: function () {
            return this.trade.qty >= this.getMinQtyToTrade() && this.trade.price !== null && this.trade.assetToTrade !== null && this.trade.assetToTradeInto !== null && !this.trade.isExecuting && this.trade.results === null;
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
            let pairs = this.account?.assetBalances?.[this.trade.assetToTrade]?.tradableAssetPairs;
            if (pairs) {
                for (let i in pairs) {
                    let pair = pairs[i];
                    if (pair.assetBought === this.trade.assetToTrade) {
                        r.push(pair.assetSold);
                    } else if (pair.assetSold === this.trade.assetToTrade) {
                        r.push(pair.assetBought);
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
                        return row.fiatValue > t.account.dustThresholdInFiat;
                    });
                }

                rows = rows.sort(function (a, b) {
                    return b.fiatValue - a.fiatValue;
                });

                return rows;
            }
        }
    },
    methods: {
        getMaxQtyToTrade: function (assetToTrade) {
            let row = this.account?.assetBalances?.[assetToTrade];
            if (row) {
                return row.qty;
            }
            return null;
        },
        getMinQtyToTrade: function (assetToTrade = this.trade.assetToTrade, assetToTradeInto = this.trade.assetToTradeInto) {
            if (assetToTrade && assetToTradeInto && this.account?.assetBalances) {
                for (let asset in this.account.assetBalances) {
                    let row = this.account.assetBalances[asset];

                    let pairCode = assetToTrade + "/" + assetToTradeInto;
                    let pairCodeReverse = assetToTradeInto + "/" + assetToTrade;

                    let pair = row.tradableAssetPairs?.[pairCode];
                    let pairReverse = row.tradableAssetPairs?.[pairCodeReverse];

                    if(pair !== null || pairReverse !== null){
                        if (pair && !pairReverse) {
                            return pair.minimumTradeQty;
                        } else if (!pair && pairReverse) {
                            // TODO price here could not be what we expect it to be...
                            let price = this.trade.priceForPair?.[pairCode];
                            if(!price){
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
            this.trade.qty = percent / 100 * this.trade.maxQtyToTrade;
        },
        openTradeModal: function (row) {
            let _this = this;
            this.trade.row = row;
            this.trade.results = null;
            this.trade.errorMsg = null;
            this.trade.assetToTrade = row.asset;
            if (row.asset === this.account.storeDefaultFiat) {
                this.trade.assetToTradeInto = "BTC";
            } else {
                this.trade.assetToTradeInto = this.account.storeDefaultFiat;
            }

            // TODO watch "this.trade.assetToTrade" for changes and if so, set "qty" to max + fill "maxQtyToTrade" and "price"

            this.trade.qty = row.qty;
            this.trade.maxQtyToTrade = row.qty;
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
            if (this.modals.withdraw === null) {
                this.modals.withdraw = new window.bootstrap.Modal('#withdrawModal');
            }
            this.modals.withdraw.show();
        },
        openDepositModal: function (row) {
            if (this.modals.deposit === null) {
                this.modals.deposit = new window.bootstrap.Modal('#depositModal');
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
            this.modals.trade._config.backdrop = 'static';
            this.modals.trade._config.keyboard = false;

            const _this = this;
            const token = document.querySelector("input[name='__RequestVerificationToken']").value;

            const response = await fetch(url, {
                method,
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token
                },
                body: JSON.stringify({
                    fromAsset: _this.trade.assetToTrade,
                    toAsset: _this.trade.assetToTradeInto,
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
            _this.modals.trade._config.backdrop = true;
            _this.modals.trade._config.keyboard = true;
            _this.trade.isExecuting = false;
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
            if (!this.trade.assetToTrade || !this.trade.assetToTradeInto) {
                // We need to know the 2 assets or we cannot do anything...
                return;
            }

            if (this.trade.assetToTrade === this.trade.assetToTradeInto) {
                // The 2 assets must be different
                this.trade.price = null;
                return;
            }

            if (this.trade.isUpdating) {
                console.log("Previous request is still running. No need to hammer the server.");
                return;
            }

            this.trade.isUpdating = true;

            let _this = this;
            var searchParams = new URLSearchParams(window.location.search);
            if (this.trade.assetToTrade) {
                searchParams.set("assetToTrade", this.trade.assetToTrade);
            }
            if (this.trade.assetToTradeInto) {
                searchParams.set("assetToTradeInto", this.trade.assetToTradeInto);
            }
            let url = window.ajaxTradePrepareUrl + "?" + searchParams.toString();

            this.trade.updateTradePriceAbortController = new AbortController();

            fetch(url, {
                    signal: this.trade.updateTradePriceAbortController.signal,
                    headers: {
                        'Content-Type': 'application/json'
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
                _this.trade.maxQtyToTrade = data.maxQtyToTrade;

                // By default trade everything
                if (_this.trade.qty === null) {
                    _this.trade.qty = _this.trade.maxQtyToTrade;
                }

                // Cannot trade more than what we have
                if (data.maxQtyToTrade < _this.trade.qty) {
                    _this.trade.qty = _this.trade.maxQtyToTrade;
                }
                let pair = data.fromAsset+"/"+data.toAsset;
                let pairReverse = data.toAsset+"/"+data.fromAsset;
                
                // TODO Should we use "bid" in some cases? The spread can be huge with some shitcoins.
                _this.trade.price = data.ask;
                _this.trade.priceForPair[pair] = data.ask;
                _this.trade.priceForPair[pairReverse] = 1 / data.ask;
                
            }).catch(function (e) {
                _this.trade.isUpdating = false;
                if (e instanceof DOMException && e.code === DOMException.ABORT_ERR) {
                    console.log("User aborted fetch request");
                } else {
                    throw e;
                }
            });
        },
        canSwapTradeAssets: function () {
            let minQtyToTrade = this.getMinQtyToTrade(this.trade.assetToTradeInto, this.trade.assetToTrade);
            let assetToTradeIntoHoldings = this.account?.assetBalances?.[this.trade.assetToTradeInto];
            if (assetToTradeIntoHoldings) {
                return assetToTradeIntoHoldings.qty >= minQtyToTrade;
            }
        },
        swapTradeAssets: function () {
            // Swap the 2 assets
            let tmp = this.trade.assetToTrade;
            this.trade.assetToTrade = this.trade.assetToTradeInto;
            this.trade.assetToTradeInto = tmp;
            this.trade.price = 1 / this.trade.price;
            
            this._refreshTradeDataAfterAssetChange();
        },
        _refreshTradeDataAfterAssetChange: function(){
            let maxQtyToTrade = this.getMaxQtyToTrade(this.trade.assetToTrade);
            this.trade.qty = maxQtyToTrade
            this.trade.maxQtyToTrade = maxQtyToTrade;

            this.killAjaxIfRunning(this.trade.updateTradePriceAbortController);

            // Update the price asap, so we can continue
            let _this = this;
            setTimeout(function () {
                _this.updateTradePrice();
            }, 100);
        },
        killAjaxIfRunning: function (abortController) {
            abortController.abort();
        },
        refreshAccountBalances: function () {
            let _this = this;
            fetch(window.ajaxBalanceUrl).then(function (response) {
                return response.json();
            }).then(function (result) {
                _this.account = result;
            });
        }
    },
    watch: {
        'trade.assetToTrade': function(newValue, oldValue){
            if(newValue === this.trade.assetToTradeInto){
                // This is the same as swapping the 2 assets
                this.trade.assetToTradeInto = oldValue;
                this.trade.price = 1 / this.trade.price;
                
                this._refreshTradeDataAfterAssetChange();
            }
            if(newValue !== oldValue){
                // The qty is going to be wrong, so set to 100%
                this.trade.qty = this.getMaxQtyToTrade(this.trade.assetToTrade);
            }
        }
    },
    created: function () {
        this.refreshAccountBalances();
    },
    mounted: function () {
        // Runs when the app is ready
    }
});
