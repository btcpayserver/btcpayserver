var vault = (function () {
    /** @param {WebSocket} websocket
    */
    function VaultBridge(websocket) {
        var self = this;
        /**
         * @type {WebSocket}
         */
        this.socket = websocket;
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
                if (event.data === "ping")
                    return;
                var jsonObject = JSON.parse(event.data);
                if (jsonObject.command == "sendRequest") {
                    var request = new XMLHttpRequest();
                    request.onreadystatechange = function () {
                        if (request.readyState == 4) {
                            if (request.status === 0) {
                                self.socket.send("{\"error\": \"Failed to connect to uri\"}");
                            }
                            else if (self.socket.readyState == 1) {
                                var body = null;
                                if (request.responseText) {
                                    var contentType = request.getResponseHeader('Content-Type') || 'text/plain';
                                    if (contentType === 'text/plain')
                                        body = request.responseText;
                                    else
                                        body = JSON.parse(request.responseText);
                                }
                                
                                self.socket.send(JSON.stringify(
                                    {
                                        httpCode: request.status,
                                        body: body
                                    }));
                            }
                        }
                    };
                    request.overrideMimeType("text/plain");
                    request.open('POST', jsonObject.uri);
                    jsonObject.body = jsonObject.body || {};
                    request.send(JSON.stringify(jsonObject.body));
                }
                else {
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
            socketError: "SocketError"
        },
        askVaultPermission: askVaultPermission,
        connectToBackendSocket: connectToBackendSocket,
        VaultBridge: VaultBridge
    };
})();
