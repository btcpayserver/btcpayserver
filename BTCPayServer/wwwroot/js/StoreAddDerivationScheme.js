function getVaultUI() {
    var websocketPath = $("#WebsocketPath").text();
    var loc = window.location, ws_uri;
    if (loc.protocol === "https:") {
        ws_uri = "wss:";
    } else {
        ws_uri = "ws:";
    }
    ws_uri += "//" + loc.host;
    ws_uri += websocketPath;
    return new vaultui.VaultBridgeUI(ws_uri);
}

function showModal() {
    var html = $("#btcpayservervault_template").html();
    $("#btcpayservervault").html(html);
    html = $("#VaultConnection").html();
    $("#vaultPlaceholder").html(html);
    $('#btcpayservervault').modal();
}

async function showAddress(rootedKeyPath, address) {
    $(".showaddress").addClass("disabled");
    showModal();
    $("#btcpayservervault #displayedAddress").text(address);
    var vaultUI = getVaultUI();
    $('#btcpayservervault').on('hidden.bs.modal', function () {
        vaultUI.closeBridge();
        $(".showaddress").removeClass("disabled");
    });
    if (await vaultUI.askForDevice())
        await vaultUI.askForDisplayAddress(rootedKeyPath);
    $('#btcpayservervault').modal("hide");
}

$(document).ready(function () {
    function displayXPubs(xpub) {
        $("#DerivationScheme").val(xpub.strategy);
        $("#RootFingerprint").val(xpub.fingerprint);
        $("#AccountKey").val(xpub.accountKey);
        $("#Source").val("Vault");
        $("#DerivationSchemeFormat").val("BTCPay");
        $("#KeyPath").val(xpub.keyPath);
        $(".modal").modal('hide');
        $(".hw-fields").show();
    }

    $(".check-for-vault").on("click", async function () {
        var vaultUI = getVaultUI();
        showModal();
        $('#btcpayservervault').on('hidden.bs.modal', function () {
            vaultUI.closeBridge();
        });
        while (! await vaultUI.askForDevice() || ! await vaultUI.askForXPubs()) {
        }
        displayXPubs(vaultUI.xpub);
    });
});
