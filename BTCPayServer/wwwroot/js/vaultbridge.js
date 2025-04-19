var vault = (function () {
        async function sendRequest(req)
        {
            
            try {
                const response = await fetch(req.uri, {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'text/plain'
                    },
                    body: JSON.stringify(req.body)
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

        async function askVaultPermission(url) {
            url = url + "/request-permission";
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
            sendRequest: sendRequest
        };
    }
)();
