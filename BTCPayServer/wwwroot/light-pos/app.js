var app = null;
var eventAggregator = new Vue();

function addLoadEvent(func) {
    var oldonload = window.onload;
    if (typeof window.onload != 'function') {
        window.onload = func;
    } else {
        window.onload = function() {
            if (oldonload) {
                oldonload();
            }
            func();
        }
    }
}
addLoadEvent(function (ev) {
    Vue.component('keypad', {
        props: ['keys'],
        template: '#keypad-template'
    });

    Vue.component('controls', {
        props: ['keys'],
        template: '#controls-template',
        methods: {
            onClickClear: function() {
                eventAggregator.$emit("clearClicked");
            }
        }
    });

    Vue.component('display', {
        props: ['value', 'currency'],
        template: '#display-template',
        data: function() {
            return {
                fontSize: 80
            }
        },
        methods: {
            getWidth: function(el) {
                var styles = window.getComputedStyle(el),
                    width = parseFloat(el.clientWidth),
                    padL = parseFloat(styles.paddingLeft),
                    padR = parseFloat(styles.paddingRight);

                return width - padL - padR;
            }
        },
        watch: {
            value: function(val) {
                var self = this;

                // This must be timeouted because the updated width is not available yet
                setTimeout(function(){
                    var displayWidth = self.getWidth(self.$refs.display),
                        amountWidth = self.getWidth(self.$refs.amount);

                    if (displayWidth <= amountWidth) {
                        var gamma = displayWidth / amountWidth || 0;
                        self.fontSize = Math.floor(self.fontSize * gamma);
                    }
                }, 10);
            }
        }
    });

    Vue.component('keypad-button', {
        props: ['text', 'newLine'],
        template: '#keypad-button-template',
        computed: {
            classObject: function () {
              return {
                    'btn btn-primary': (isNaN(this.text) === false) || this.text === '.',
                    'btn btn-dark': isNaN(this.text) && this.text !== '.',
              }
            }
        },
        methods: {
            onClickButton: function() {
                eventAggregator.$emit("buttonClicked", this.text);
            }
        }
    });
    
    app = new Vue({
        el: '#app',
        data: function(){
            return {
                srvModel: window.srvModel,
                payTotal: '0',
                payTotalNumeric: 0,
                keys: ['1', '2', '3', '4', '5', '6', '7', '8', '9', '.', '0', 'C']
            }
        },
        computed: {
            Currency: function(){
                return this.srvModel.Currency.toUpperCase();
            },
        },
        methods: {
            clearTotal: function() {
                this.payTotal = '0';
                this.payTotalNumeric = 0;
            }
        },
        mounted: function () {
            var self = this;

            eventAggregator.$on("buttonClicked", function(key) {
                var payTotal = self.payTotal;

                if (key === 'C') {
                    payTotal = payTotal.substring(0, payTotal.length - 1);
                    payTotal = payTotal === '' ? '0' : payTotal;
                } else if (key === '.') {
                    // Only add decimal point if it doesn't exist yet
                    if (payTotal.indexOf('.') === -1) {
                        payTotal += key;
                    }
                } else { // Is a digit
                    if (payTotal === '0') {
                        payTotal = '';
                    }
                    payTotal += key;
                }

                self.payTotal = payTotal;
                self.payTotalNumeric = parseFloat(payTotal);
            });

            eventAggregator.$on("clearClicked", function(key) {
                self.clearTotal();
            });
        }
    });
});

