var app = null;
var eventAggregator = new Vue();
window.onload = function (ev) {
    

    app = new Vue({
        el: '#app',
        data: function () {
            return {
                srvModel: window.srvModel
            }
        },
        mounted: function () {
            hubListener.connect();
        }
    });
};

