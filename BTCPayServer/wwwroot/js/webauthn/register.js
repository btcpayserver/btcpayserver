async function register(makeCredentialOptions) {
    console.debug("Credential Options Object", makeCredentialOptions);
    // Turn the challenge back into the accepted format of padded base64
    makeCredentialOptions.challenge = coerceToArrayBuffer(makeCredentialOptions.challenge);
    // Turn ID into a UInt8Array Buffer for some reason
    makeCredentialOptions.user.id = coerceToArrayBuffer(makeCredentialOptions.user.id);

    makeCredentialOptions.excludeCredentials = makeCredentialOptions.excludeCredentials.map((c) => {
        c.id = coerceToArrayBuffer(c.id);
        return c;
    });

    if (makeCredentialOptions.authenticatorSelection.authenticatorAttachment == null) makeCredentialOptions.authenticatorSelection.authenticatorAttachment = undefined;

    console.debug("Credential Options Formatted", makeCredentialOptions);
    console.debug("Creating PublicKeyCredential...");

    let newCredential;
    try {
        newCredential = await navigator.credentials.create({
            publicKey: makeCredentialOptions
        });
    } catch (e) {
        var msg = "Could not create credentials in browser. Probably because the username is already registered with your authenticator. Please change username or authenticator."
        showErrorAlert(msg, e);
        return;
    }

    console.debug("PublicKeyCredential Created", newCredential);

    try {
        registerNewCredential(newCredential);

    } catch (e) {
        showErrorAlert(err.message ? err.message : err);
    }
}

// This should be used to verify the auth data with the server
async function registerNewCredential(newCredential) {
    // Move data into Arrays incase it is super long
    let attestationObject = new Uint8Array(newCredential.response.attestationObject);
    let clientDataJSON = new Uint8Array(newCredential.response.clientDataJSON);
    let rawId = new Uint8Array(newCredential.rawId);

    const data = {
        id: newCredential.id,
        rawId: coerceToBase64Url(rawId),
        type: newCredential.type,
        extensions: newCredential.getClientExtensionResults(),
        response: {
            AttestationObject: coerceToBase64Url(attestationObject),
            clientDataJson: coerceToBase64Url(clientDataJSON)
        }
    };
    
    document.getElementById("data").value = JSON.stringify(data);
    document.getElementById("registerForm").submit();
}

document.addEventListener('DOMContentLoaded', () => {
    if (detectFIDOSupport() && makeCredentialOptions) {
        const infoMessage = document.getElementById("info-message");
        const startButton = document.getElementById("btn-start");
        if (isSafari()) {
            startButton.addEventListener("click", ev => {
                register(makeCredentialOptions);
                infoMessage.classList.remove("d-none");
                startButton.classList.add("d-none");
            });
            startButton.classList.remove("d-none");
        } else {
            infoMessage.classList.remove("d-none");
            register(makeCredentialOptions);
        }
    }
})
