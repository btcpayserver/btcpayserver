Vue.directive('collapsible', {
    bind: function (el, binding) {
        el.classList.add('collapse');
        el.classList[binding.value ? 'add' : 'remove']('show');
        el.transitionDuration = 350;
    },
    update: function (el, binding) {
        if (binding.oldValue !== binding.value){
            if (binding.value) {
                setTimeout(function () {
                    el.classList.remove('collapse');
                    const height = window.getComputedStyle(el).height;
                    el.classList.add('collapsing');
                    el.offsetHeight;
                    el.style.height = height;
                    setTimeout(function () {
                        el.classList.remove('collapsing');
                        el.classList.add('collapse');
                        el.style.height = null;
                        el.classList.add('show');
                    }, el.transitionDuration)
                }, 0);
            } else {
                el.style.height = window.getComputedStyle(el).height;
                el.classList.remove('collapse');
                el.classList.remove('show');
                el.offsetHeight;
                el.style.height = null;
                el.classList.add('collapsing');
                setTimeout(function () {
                    el.classList.add('collapse');
                    el.classList.remove('collapsing');
                }, el.transitionDuration)
            }
        }
    }
});

const fallbackLanguage = 'en';
const startingLanguage = computeStartingLanguage();
const STATUS_PAID = ['complete', 'confirmed', 'paid'];
const STATUS_UNPAYABLE =  ['expired', 'invalid'];

function computeStartingLanguage() {
    const { defaultLang } = initialSrvModel;
    return isLanguageAvailable(defaultLang) ? defaultLang : fallbackLanguage;
}

function isLanguageAvailable(languageCode) {
    return availableLanguages.indexOf(languageCode) >= 0;
}

Vue.use(VueI18next);

const i18n = new VueI18next(i18next);
const eventBus = new Vue();

const PaymentDetails = Vue.component('payment-details', {
    el: '#payment-details',
    props: {
        srvModel: Object,
        isActive: Boolean
    },
    computed: {
        orderAmount () {
            return parseFloat(this.srvModel.orderAmount);
        },
        btcDue () {
            return parseFloat(this.srvModel.btcDue);
        },
        btcPaid () {
            return parseFloat(this.srvModel.btcPaid);
        },
        showRecommendedFee () {
            return this.isActive && this.srvModel.showRecommendedFee && this.srvModel.feeRate;
        },
    }
});

function initApp() {
    return new Vue({
        i18n,
        el: '#Checkout',
        components: {
            PaymentDetails
        },
        data () {
            const srvModel = initialSrvModel;
            return {
                srvModel,
                displayPaymentDetails: false,
                remainingSeconds: srvModel.expirationSeconds,
                expirationPercentage: 0,
                emailAddressInput: "",
                emailAddressInputDirty: false,
                emailAddressInputInvalid: false,
                paymentMethodId: null,
                endData: null,
                isModal: srvModel.isModal
            }
        },
        computed: {
            isUnpayable () {
                return STATUS_UNPAYABLE.includes(this.srvModel.status);
            },
            isPaid () {
                return STATUS_PAID.includes(this.srvModel.status);
            },
            isActive () {
                return !this.isUnpayable && !this.isPaid;
            },
            showInfo () {
                return this.showTimer || this.showPaymentDueInfo;
            },
            showTimer () {
                return this.isActive && (this.expirationPercentage >= 75 || this.minutesLeft < 5);
            },
            showPaymentDueInfo () {
                return this.btcPaid > 0 && this.btcDue > 0;
            },
            showRecommendedFee () {
                return this.isActive() && this.srvModel.showRecommendedFee && this.srvModel.feeRate;
            },
            orderAmount () {
                return parseFloat(this.srvModel.orderAmount);
            },
            btcDue () {
                return parseFloat(this.srvModel.btcDue);
            },
            btcPaid () {
                return parseFloat(this.srvModel.btcPaid);
            },
            pmId () {
                return this.paymentMethodId || this.srvModel.paymentMethodId;
            },
            minutesLeft () {
                return Math.floor(this.remainingSeconds / 60);
            },
            secondsLeft () {
                return Math.floor(this.remainingSeconds % 60);
            },
            timeText () {
                return this.remainingSeconds > 0
                    ? `${this.padTime(this.minutesLeft)}:${this.padTime(this.secondsLeft)}`
                    : '00:00';
            },
            storeLink () {
                return this.srvModel.merchantRefLink && this.srvModel.merchantRefLink !== this.srvModel.receiptLink
                    ? this.srvModel.merchantRefLink
                    : null;
            },
            paymentMethodIds () {
                return this.srvModel.availableCryptos.map(function (c) { return c.paymentMethodId });
            },
            paymentMethodComponent () {
                return this.isPluginPaymentMethod
                    ? `${this.pmId}Checkout`
                    : this.srvModel.activated && this.srvModel.uiSettings.checkoutBodyVueComponentName;
            },
            isPluginPaymentMethod () {
                return !this.paymentMethodIds.includes(this.pmId);
            }
        },
        mounted () {
            this.updateData(this.srvModel);
            this.updateTimer();
            if (this.isActive) {
                this.listenIn();
            }
            window.parent.postMessage('loaded', '*');
        },
        methods: {
            changePaymentMethod (id) { // payment method or plugin id
                if (this.pmId !== id) {
                    this.paymentMethodId = id;
                    this.fetchData();
                }
            },
            changeLanguage (e) {
                const lang = e.target.value;
                if (isLanguageAvailable(lang)) {
                    i18next.changeLanguage(lang);
                }
            },
            padTime (val) {
                return val.toString().padStart(2, '0');
            },
            close () {
                window.parent.postMessage('close', '*');
            },
            updateTimer () {
                this.remainingSeconds = Math.floor((this.endDate.getTime() - new Date().getTime())/1000);
                this.expirationPercentage = 100 - Math.floor((this.remainingSeconds / this.srvModel.maxTimeSeconds) * 100);
                if (this.isActive) {
                    setTimeout(this.updateTimer, 500);
                }
            },
            listenIn () {
                let socket = null;
                const updateFn = this.fetchData;
                const supportsWebSockets = 'WebSocket' in window && window.WebSocket.CLOSING === 2;
                if (supportsWebSockets) {
                    const protocol = window.location.protocol.replace('http', 'ws');
                    const wsUri = `${protocol}//${window.location.host}${statusWsUrl}`;
                    try {
                        socket = new WebSocket(wsUri);
                        socket.onmessage = async function (e) {
                            if (e.data !== 'ping') await updateFn();
                        };
                        socket.onerror = function (e) {
                            console.error('Error while connecting to websocket for invoice notifications (callback):', e);
                        };
                    }
                    catch (e) {
                        console.error('Error while connecting to websocket for invoice notifications', e);
                    }
                }
                // fallback in case there is no websocket support
                (function watcher() {
                    setTimeout(async function () {
                        if (socket === null || socket.readyState !== 1) {
                            await updateFn();
                        }
                        watcher();
                    }, 2000);
                })();
            },
            async fetchData () {
                if (this.isPluginPaymentMethod) return;
                
                const url = `${statusUrl}&paymentMethodId=${this.pmId}`;
                const response = await fetch(url);
                if (response.ok) {
                    const data = await response.json();
                    this.updateData(data);
                }
            },
            updateData (data) {
                if (this.srvModel.status !== data.status) {
                    const { invoiceId } = this.srvModel;
                    const { status } = data;
                    window.parent.postMessage({ invoiceId, status }, '*');
                }
    
                // displaying satoshis for lightning payments
                data.cryptoCodeSrv = data.cryptoCode;
    
                const newEnd = new Date();
                newEnd.setSeconds(newEnd.getSeconds() + data.expirationSeconds);
                this.endDate = newEnd;
    
                // updating ui
                this.srvModel = data;
                eventBus.$emit('data-fetched', this.srvModel);
    
                const self = this;
                if (this.isPaid && data.redirectAutomatically && data.merchantRefLink) {
                    setTimeout(function () {
                        if (self.isModal && window.top.location === data.merchantRefLink){
                            self.close();
                        } else {
                            window.top.location = data.merchantRefLink;
                        }
                    }, 2000);
                }
            },
            replaceNewlines (value) {
                return value ? value.replace(/\n/ig, '<br>') : '';
            }
        }
    });
}

i18next
    .use(window.i18nextHttpBackend)
    .init({
        backend: {
            loadPath: i18nUrl
        },
        lng: startingLanguage,
        fallbackLng: fallbackLanguage,
        nsSeparator: false,
        keySeparator: false,
        load: 'currentOnly'
    }, initApp);
