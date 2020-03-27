$(function () {
    new Vue({
        el: '#wallet-camera-app',
        data: {
            noStreamApiSupport: false,
            loaded: false,
            data: "",
            errorMessage: ""
        },
        mounted: function () {
            var self = this;
            $("#scanqrcode").click(function () {
                self.loaded = true;
            });
        },
        computed: {
            camera: function () {
                return this.data ? "off" : "auto";
            }
        },
        methods: {
            retry: function () {
                if (!this.data) {
                    this.close();
                    this.$nextTick(function () {
                        this.loaded = true;
                    });
                    return;
                }
                this.data = "";
            },
            close: function () {
                this.loaded = false;
                this.data = "";
                this.errorMessage = "";
            },
            onDecode(content) {
                this.data = decodeURIComponent(content);
            },
            submitData: function () {
                $("#BIP21").val(this.data);
                $("form").submit();
                this.close();
            },
            logErrors: function (promise) {
                promise.catch(console.error)
            },
            paint: function (location, ctx) {
                ctx.fillStyle = '#137547';
                [
                    location.topLeftFinderPattern,
                    location.topRightFinderPattern,
                    location.bottomLeftFinderPattern
                ].forEach(({x, y}) => {
                    ctx.fillRect(x - 5, y - 5, 10, 10);
                })
            },
            onInit: function (promise) {
                var self = this;
                promise.then(() => {
                    self.errorMessage = "";
                })
                    .catch(error => {
                        if (error.name === 'StreamApiNotSupportedError') {
                            self.noStreamApiSupport = true;
                        } else if (error.name === 'NotAllowedError') {
                            self.errorMessage = 'A permission to the camera is needed to scan the QR code.'
                        } else if (error.name === 'NotFoundError') {
                            self.errorMessage = 'A camera was not detected on your device.'
                        } else if (error.name === 'NotSupportedError') {
                            self.errorMessage = 'This page is served in non-secure context (HTTPS, localhost or file://)'
                        } else if (error.name === 'NotReadableError') {
                            self.errorMessage = 'Couldn\'t access your camera. Is it already in use?'
                        } else if (error.name === 'OverconstrainedError') {
                            self.errorMessage = 'Constraints don\'t match any installed camera.'
                        } else {
                            self.errorMessage = 'UNKNOWN ERROR: ' + error.message
                        }
                    })
            }
        }
    });
});
