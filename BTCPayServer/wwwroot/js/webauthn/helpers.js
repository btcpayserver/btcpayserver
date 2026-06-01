coerceToArrayBuffer = function (thing, name) {
    if (typeof thing === "string") {
        // base64url to base64
        thing = thing.replace(/-/g, "+").replace(/_/g, "/");

        // base64 to Uint8Array
        var str = window.atob(thing);
        var bytes = new Uint8Array(str.length);
        for (var i = 0; i < str.length; i++) {
            bytes[i] = str.charCodeAt(i);
        }
        thing = bytes;
    }

    // Array to Uint8Array
    if (Array.isArray(thing)) {
        thing = new Uint8Array(thing);
    }

    // Uint8Array to ArrayBuffer
    if (thing instanceof Uint8Array) {
        thing = thing.buffer;
    }

    // error if none of the above worked
    if (!(thing instanceof ArrayBuffer)) {
        throw new TypeError("could not coerce '" + name + "' to ArrayBuffer");
    }

    return thing;
};


coerceToBase64Url = function (thing) {
    // Array or ArrayBuffer to Uint8Array
    if (Array.isArray(thing)) {
        thing = Uint8Array.from(thing);
    }

    if (thing instanceof ArrayBuffer) {
        thing = new Uint8Array(thing);
    }

    // Uint8Array to base64
    if (thing instanceof Uint8Array) {
        var str = "";
        var len = thing.byteLength;

        for (var i = 0; i < len; i++) {
            str += String.fromCharCode(thing[i]);
        }
        thing = window.btoa(str);
    }

    if (typeof thing !== "string") {
        throw new Error("could not coerce to string");
    }

    // base64 to base64url
    // NOTE: "=" at the end of challenge is optional, strip it off here
    thing = thing.replace(/\+/g, "-").replace(/\//g, "_").replace(/=*$/g, "");

    return thing;
};



// HELPERS

function showErrorAlert(message, error) {
    let footermsg = '';
    if (error) {
        footermsg = 'exception:' + error.toString();
    }
    console.error(message, footermsg);

    const $info = document.getElementById("info-message");
    if ($info) $info.classList.add("d-none");
    document.getElementById("btn-retry").classList.remove("d-none");
    document.getElementById("error-message").textContent = message;
    for(let el of document.getElementsByClassName("fido-running")){
        el.classList.add("d-none");
    }
    document.getElementById("error-message").classList.remove("d-none");
}

function detectFIDOSupport() {
    if (window.PublicKeyCredential === undefined ||
        typeof window.PublicKeyCredential !== "function") {
        const el = document.getElementById("error-message");
        el.textContent = location.protocol === "http:"
            ? "FIDO2/WebAuthN requires HTTPS"
            : "Your browser does not support FIDO2/WebAuthN";
        el.classList.remove("d-none");
        return false;
    }
    return true;
}

/**
 *
 * Get a form value
 * @param {any} selector
 */
function value(selector) {
    var el = document.querySelector(selector);
    if (el.type === "checkbox") {
        return el.checked;
    }
    return el.value;
}
function isSafari(){
    //https://stackoverflow.com/a/23522755/275504
    return  /^((?!chrome|android).)*safari/i.test(navigator.userAgent);
}

function detectPasskeySupport() {
    if (window.PublicKeyCredential === undefined ||
        typeof window.PublicKeyCredential !== "function") {
        return false;
    }
    return true;
}

// Initialize passkey login button on the login page
function initPasskeyLogin() {
    if (!detectPasskeySupport()) {
        console.log("Passkey not supported in this browser");
        return;
    }

    // Show the passkey login elements
    const button = document.getElementById("passkey-login-btn");
    if (button) {
        button.style.display = "";
        button.addEventListener("click", startPasskeyLogin);
    }
}

// Start the passkey login flow
async function startPasskeyLogin() {
    const button = document.getElementById("passkey-login-btn");
    const errorEl = document.getElementById("passkey-error");
    const loadingEl = document.getElementById("passkey-loading");

    // Hide any previous errors
    if (errorEl) errorEl.style.display = "none";

    // Show loading state
    if (button) button.disabled = true;
    if (loadingEl) loadingEl.style.display = "";

    try {

        const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
        const headers = { 'Content-Type': 'application/json' };
        if (tokenInput?.value) headers['RequestVerificationToken'] = tokenInput.value;

        // Request authentication options from server
        const optionsResponse = await fetch(passKeyOptionsUrl, {
            method: "POST",
            headers: headers
        });

        if (!optionsResponse.ok) {
            throw new Error("Failed to get passkey options");
        }

        const options = await optionsResponse.json();

        // Convert challenge from base64url to ArrayBuffer
        options.challenge = coerceToArrayBuffer(options.challenge, "challenge");

        // Convert allowCredentials if present
        if (options.allowCredentials) {
            options.allowCredentials = options.allowCredentials.map(cred => {
                cred.id = coerceToArrayBuffer(cred.id, "allowCredentials.id");
                return cred;
            });
        }

        // Perform WebAuthn authentication
        let credential;
        try {
            credential = await navigator.credentials.get({ publicKey: options });
        } catch (err) {
            if (err.name === "NotAllowedError") {
                throw new Error("Authentication was cancelled or timed out");
            }
            throw err;
        }

        // Submit the credential to the server
        await submitPasskeyCredential(credential);

    } catch (error) {
        console.error("Passkey login error:", error);
        showPasskeyError(error.message || "Passkey authentication failed");
    } finally {
        // Reset loading state
        if (button) button.disabled = false;
        if (loadingEl) loadingEl.style.display = "none";
    }
}

// Submit the passkey credential to the server
async function submitPasskeyCredential(credential) {
    // Prepare the assertion response
    const authData = new Uint8Array(credential.response.authenticatorData);
    const clientDataJSON = new Uint8Array(credential.response.clientDataJSON);
    const rawId = new Uint8Array(credential.rawId);
    const sig = new Uint8Array(credential.response.signature);

    const data = {
        id: credential.id,
        rawId: coerceToBase64Url(rawId),
        type: credential.type,
        extensions: credential.getClientExtensionResults(),
        response: {
            authenticatorData: coerceToBase64Url(authData),
            clientDataJSON: coerceToBase64Url(clientDataJSON),
            signature: coerceToBase64Url(sig)
        }
    };

    // Add userHandle if present (for discoverable credentials)
    if (credential.response.userHandle) {
        data.response.userHandle = coerceToBase64Url(new Uint8Array(credential.response.userHandle));
    }

    // Submit via hidden form (to include anti-forgery token)
    document.getElementById("PasskeyResponse").value = JSON.stringify(data);
    document.querySelector('#login-password-fieldset').disabled = true;
    document.getElementById("PasskeyButton").click();
}

// Show an error message
function showPasskeyError(message) {
    const errorEl = document.getElementById("passkey-error");
    if (errorEl) {
        errorEl.textContent = message;
        errorEl.style.display = "";
    }
}

// Initialize on page load
document.addEventListener("DOMContentLoaded", initPasskeyLogin);
