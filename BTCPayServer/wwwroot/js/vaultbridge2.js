var vault = (function () {
        async function sendHwi(req) {
            const url = "http://127.0.0.1:65092/hwi-bridge/v1";
            
            try {
                const response = await fetch(url, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'text/plain'
                    },
                    body: JSON.stringify(req)
                });

                if (!response.ok) {
                    return { httpCode: response.status };
                }

                const contentType = response.headers.get('Content-Type') || 'text/plain';
                const body = contentType.includes('application/json')
                    ? await response.json()
                    : await response.text();
                return { httpCode: response.status, body };
            } catch (e) {
                return { httpCode: 0, error: e.message };
            }
        }

        async function askVaultPermission() {
            const url = 'http://127.0.0.1:65092/hwi-bridge/v1/request-permission';
            var browser = "other";
            if (window.safari !== undefined)
                browser = "safari";
            if (navigator.brave !== undefined)
                browser = "brave";
            try {
                const response = await fetch(url, {
                    method: 'GET',
                    headers: {
                        'Accept': 'text/plain'
                    }
                });
                return {httpCode: response.status, browser: browser};
            } catch (e) {
                return {httpCode: 0, browser: browser};
            }
        }

        return {
            askVaultPermission: askVaultPermission,
            sendHwi: sendHwi
        };
    }
)();
