/**
 * Passkey (passwordless) login functionality
 * Enables users to sign in using WebAuthn/FIDO2 passkeys without entering a password
 */

// Check if passkey login is supported
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

    // Check if we're on a secure connection
    if (location.protocol !== "https:" && location.hostname !== "localhost") {
        console.log("Passkey requires HTTPS");
        return;
    }

    // Show the passkey login elements
    const separator = document.getElementById("passkey-separator");
    const button = document.getElementById("passkey-login-btn");
    const errorEl = document.getElementById("passkey-error");

    if (separator) separator.style.display = "";
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
        // Get email if entered (optional for discoverable flow)
        const emailInput = document.getElementById("Email");
        const email = emailInput?.value?.trim() || null;

        // Request authentication options from server
        const optionsResponse = await fetch("/login/passkey/options", {
            method: "POST",
            headers: {
                "Content-Type": "application/json"
            },
            body: JSON.stringify({ email: email })
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
    const form = document.getElementById("passkey-form");
    const responseInput = document.getElementById("PasskeyResponse");
    const rememberMeInput = document.getElementById("PasskeyRememberMe");
    const loginRememberMe = document.getElementById("RememberMe");

    if (form && responseInput) {
        responseInput.value = JSON.stringify(data);
        if (rememberMeInput && loginRememberMe) {
            rememberMeInput.value = loginRememberMe.checked ? "true" : "false";
        }
        form.submit();
    } else {
        throw new Error("Passkey form not found");
    }
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
