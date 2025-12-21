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
