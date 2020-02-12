var vault = (function () {
    /** @param {WebSocket} websocket
    */
    function VaultBridge(websocket) {
        var self = this;
        /**
         * @type {WebSocket}
         */
        this.socket = websocket;
        this.onerror = function (error) { };
        this.onbackendmessage = function (json) { };
        this.close = function () { if (websocket) websocket.close(); };
        /**
        * @returns {Promise}
        */
        this.waitBackendMessage = function () {
            return new Promise(function (resolve, reject) {
                self.nextResolveBackendMessage = resolve;
            });
        };
        this.socket.onmessage = function (event) {
            if (typeof event.data === "string") {
                var jsonObject = JSON.parse(event.data);
                if (jsonObject.hasOwnProperty("params")) {
                    var request = new XMLHttpRequest();
                    request.onreadystatechange = function () {
                        if (request.readyState == 4 && request.status == 200) {
                            self.socket.send(request.responseText);
                        }
                        if (request.readyState == 4 && request.status == 0) {
                            self.onerror(vault.errors.notRunning);
                        }
                        if (request.readyState == 4 && request.status == 401) {
                            self.onerror(vault.errors.denied);
                        }
                    };
                    request.overrideMimeType("text/plain");
                    request.open('POST', 'http://127.0.0.1:65092/hwi-bridge/v1');
                    request.send(JSON.stringify(jsonObject));
                }
                else {
                    self.onbackendmessage(jsonObject);
                    if (self.nextResolveBackendMessage)
                        self.nextResolveBackendMessage(jsonObject);
                }
            }
        };
    }

    /**
     * @param {string} ws_uri
     * @returns {Promise<VaultBridge>}
     */
    function connectToBackendSocket(ws_uri) {
        return new Promise(function (resolve, reject) {
            var supportWebSocket = "WebSocket" in window && window.WebSocket.CLOSING === 2;
            if (!supportWebSocket) {
                reject(vault.errors.socketNotSupported);
                return;
            }
            var socket = new WebSocket(ws_uri);
            socket.onerror = function (error) {
                console.warn(error);
                reject(vault.errors.socketError);
            };
            socket.onopen = function () {
                resolve(new vault.VaultBridge(socket));
            };
        });
    }

    /**
     * @returns {Promise}
     */
    function askVaultPermission() {
        return new Promise(function (resolve, reject) {
            var request = new XMLHttpRequest();
            request.onreadystatechange = function () {
                if (request.readyState == 4 && request.status == 200) {
                    resolve();
                }
                if (request.readyState == 4 && request.status == 0) {
                    reject(vault.errors.notRunning);
                }
                if (request.readyState == 4 && request.status == 401) {
                    reject(vault.errors.denied);
                }
            };
            request.overrideMimeType("text/plain");
            request.open('GET', 'http://127.0.0.1:65092/hwi-bridge/v1/request-permission');
            request.send();
        });
    }

    return {
        errors: {
            notRunning: "NotRunning",
            denied: "Denied",
            socketNotSupported: "SocketNotSupported",
            socketError: "SocketError",
        },
        askVaultPermission: askVaultPermission,
        connectToBackendSocket: connectToBackendSocket,
        VaultBridge: VaultBridge
    };
})();
