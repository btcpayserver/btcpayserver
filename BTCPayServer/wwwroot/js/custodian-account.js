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
            updateTradePriceXhr: null,
            priceRefresherInterval: null,
            assetToTrade: null,
            assetToTradeInto: null,
            qty: null,
            maxQtyToTrade: null,
            price: null
        }
    },
    computed: {
        tradeQtyToReceive: function () {
            return this.trade.qty / this.trade.price;
        },
        canExecuteTrade: function () {
            return this.trade.qty >= this.minQtyToTrade && this.trade.price !== null && this.trade.assetToTrade !== null && this.trade.assetToTradeInto !== null && !this.trade.isExecuting && this.trade.results === null;
        },
        minQtyToTrade: function() {
            if (this.account?.assetBalances) {
                for (let asset in this.account.assetBalances) {
                    let row = this.account.assetBalances[asset];
                    
                    let pairCode = this.trade.assetToTrade + "/" + this.trade.assetToTradeInto;
                    let pairCodeReverse = this.trade.assetToTradeInto + "/" + this.trade.assetToTrade;
                    
                    let pair = row.tradableAssetPairs?.[pairCode];
                    let pairReverse = row.tradableAssetPairs?.[pairCodeReverse];

                    if (pair) {
                        return pair.minimumTradeQty;
                    }else if(pairReverse){
                        return this.trade.price * pairReverse.minimumTradeQty;
                    }
                }
            }
            return 0;
        },
        availableAssetsToTrade: function () {
            let r = [];
            if (this.account?.assetBalances) {
                r = Object.keys(this.account?.assetBalances);
            }
            return r;
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
            return r;
        },
        sortedAssetRows: function () {
            if (this.account?.assetBalances) {
                let rows = Object.values(this.account.assetBalances);
                let t = this;

                if(this.hideDustAmounts) {
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
                    'Content-Type' : 'application/json',
                    'RequestVerificationToken': token
                },
                body: JSON.stringify({
                    fromAsset: _this.trade.assetToTrade,
                    toAsset: _this.trade.assetToTradeInto,
                    qty: _this.trade.qty
                })
            });
            
            let data = null;
            try { data = await response.json(); } catch (e) {}

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

            this.trade.isUpdating = true;

            if (this.isAjaxRunning(this.trade.updateTradePriceXhr)) {
                console.log("Previous request is still running. No need to hammer the server.");
                return;
            }

            let _this = this;
            this.trade.updateTradePriceXhr = window.jQuery.ajax({
                method: "get",
                url: window.ajaxTradePrepareUrl,
                data: {
                    assetToTrade: _this.trade.assetToTrade,
                    assetToTradeInto: _this.trade.assetToTradeInto
                },
                success: function (data) {
                    _this.trade.maxQtyToTrade = data.maxQtyToTrade;

                    // By default trade everything
                    if (_this.trade.qty === null) {
                        _this.trade.qty = _this.trade.maxQtyToTrade;
                    }

                    // Cannot trade more than what we have
                    if (data.maxQtyToTrade < _this.trade.qty) {
                        _this.trade.qty = _this.trade.maxQtyToTrade;
                    }

                    _this.trade.price = data.price;
                    // _this.trade.results = data;
                    // _this.trade.errorMsg = null;
                },
                complete: function () {
                    _this.trade.isUpdating = false;
                },
                error: function (xhr, textStatus, errorThrown) {
                    // Do nothing
                }
            });
        },

        swapTradeAssets: function () {
            let tmp = this.trade.assetToTrade;
            this.trade.assetToTrade = this.trade.assetToTradeInto;
            this.trade.assetToTradeInto = tmp;
            this.trade.qty = null;
            this.trade.price = 1 / this.trade.price;

            this.killAjaxIfRunning(this.trade.updateTradePriceXhr);

            // Update the price asap, so we can continue
            this.updateTradePrice();
        },
        isAjaxRunning: function (xhr) {
            return xhr && xhr.readyState > 0 && xhr.readyState < 4;
        },
        killAjaxIfRunning: function (xhr) {
            if (this.isAjaxRunning(xhr)) {
                xhr.abort();
            }
        },
        refreshAccountBalances: function(){
            let _this = this;
            fetch(window.ajaxBalanceUrl).then(function (response) {
                return response.json();
            }).then(function (result) {
                _this.account = result;
            });
        }
    },
    created: function () {
        this.refreshAccountBalances();
    },

    mounted: function () {
        // Runs when the app is ready

    }
});
