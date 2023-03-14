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

const STATUS_PAID = ['complete', 'confirmed', 'paid'];
const STATUS_UNPAYABLE =  ['expired', 'invalid'];
const urlParams = new URLSearchParams(window.location.search);

function computeStartingLanguage() {
    const lang = urlParams.get('lang')
    if (lang && isLanguageAvailable(lang)) return lang;
    const { defaultLang } = initialSrvModel;
    return isLanguageAvailable(defaultLang) ? defaultLang : fallbackLanguage;
}

function isLanguageAvailable(languageCode) {
    return availableLanguages.includes(languageCode);
}

function updateLanguageSelect() {
    // calculate and set width, as we want it center aligned
    const $languageSelect = document.getElementById('DefaultLang');
    const element = document.createElement('div');
    element.innerText = $languageSelect.querySelector('option:checked').text;
    $languageSelect.parentElement.appendChild(element);
    const width = element.offsetWidth;
    $languageSelect.parentElement.removeChild(element);
    $languageSelect.style.setProperty('--text-width', `${width}px`);
}

function updateLanguage(lang) {
    if (isLanguageAvailable(lang)) {
        i18next.changeLanguage(lang);
        urlParams.set('lang', lang);
        window.history.replaceState({}, '', `${location.pathname}?${urlParams}`);
        updateLanguageSelect();
    }
}

Vue.use(VueI18next);

const fallbackLanguage = 'en';
const startingLanguage = computeStartingLanguage();
const i18n = new VueI18next(i18next);
const eventBus = new Vue();

const PaymentDetails = {
    template: '#payment-details',
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
}

function initApp() {
    return new Vue({
        i18n,
        el: '#Checkout-v2',
        components: {
            'payment-details': PaymentDetails,
        },
        data () {
            const srvModel = initialSrvModel;
            return {
                srvModel,
                displayPaymentDetails: false,
                remainingSeconds: srvModel.expirationSeconds,
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
                return this.isActive && this.remainingSeconds < this.srvModel.displayExpirationTimer;
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
        watch: {
            isPaid: function (newValue, oldValue) {
                if (newValue === true && oldValue === false) {
                    const duration = 5000;
                    const self = this;
                    // celebration!
                    Vue.nextTick(function () {
                        self.celebratePayment(duration);
                    });
                    // automatic redirect or close
                    if (self.srvModel.redirectAutomatically && self.storeLink) {
                        setTimeout(function () {
                            if (self.isModal && window.top.location === self.storeLink) {
                                self.close();
                            } else {
                                window.top.location = self.storeLink;
                            }
                        }, duration);
                    }
                }
            }
        },
        mounted () {
            this.updateData(this.srvModel);
            this.updateTimer();
            if (this.isActive) {
                this.listenIn();
            }
            updateLanguageSelect();
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
                updateLanguage(e.target.value);
            },
            padTime (val) {
                return val.toString().padStart(2, '0');
            },
            close () {
                window.parent.postMessage('close', '*');
            },
            updateTimer () {
                this.remainingSeconds = Math.floor((this.endDate.getTime() - new Date().getTime())/1000);
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
                
                const newEnd = new Date();
                newEnd.setSeconds(newEnd.getSeconds() + data.expirationSeconds);
                this.endDate = newEnd;
    
                // updating ui
                this.srvModel = data;
                eventBus.$emit('data-fetched', this.srvModel);
            },
            replaceNewlines (value) {
                return value ? value.replace(/\n/ig, '<br>') : '';
            },
            async celebratePayment (duration) {
                const $confettiEl = document.getElementById('confetti')
                if (window.confetti && $confettiEl && !$confettiEl.dataset.running) {
                    $confettiEl.dataset.running = true;
                    await window.confetti($confettiEl, {
                        duration,
                        spread: 90,
                        stagger: 5,
                        elementCount: 121,
                        colors: ["#a864fd", "#29cdff", "#78ff44", "#ff718d", "#fdff6a"]
                    });
                    delete $confettiEl.dataset.running;
                }
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
