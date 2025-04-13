using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Lightning;
using BTCPayServer.Models;
using BTCPayServer.NTag424;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Html;
using static BTCPayServer.BoltcardDataExtensions;

namespace BTCPayServer.Controllers
{
    public partial class UIPullPaymentController
    {
        [AllowAnonymous]
        [HttpGet("pull-payments/{pullPaymentId}/boltcard/{command}")]
        public IActionResult SetupBoltcard(string pullPaymentId, string command)
        {
            return View(nameof(SetupBoltcard), new SetupBoltcardViewModel
            {
                ReturnUrl = Url.Action(nameof(ViewPullPayment), "UIPullPayment", new { pullPaymentId }),
                WebsocketPath = Url.Action(nameof(VaultNFCBridgeConnection), "UIPullPayment", new { pullPaymentId }),
                Command = command
            });
        }
        [AllowAnonymous]
        [HttpPost("pull-payments/{pullPaymentId}/boltcard/{command}")]
        public IActionResult SetupBoltcardPost(string pullPaymentId, string command)
        {
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Boltcard is configured"].Value;
            return RedirectToAction(nameof(ViewPullPayment), new { pullPaymentId });
        }

        record CardOrigin
        {
            public record Blank() : CardOrigin;
            public record ThisIssuer(BoltcardRegistration Registration) : CardOrigin;
            public record ThisIssuerConfigured(string PullPaymentId, BoltcardRegistration Registration) : ThisIssuer(Registration);
            public record OtherIssuer() : CardOrigin;
            public record ThisIssuerReset(BoltcardRegistration Registration) : ThisIssuer(Registration);
        }

        [Route("pull-payments/{pullPaymentId}/nfc/bridge")]
        public async Task<IActionResult> VaultNFCBridgeConnection(string pullPaymentId)
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
                return NotFound();
            var pp = await _pullPaymentHostedService.GetPullPayment(pullPaymentId, false);
            if (pp is null)
                return NotFound();
            if (!_pullPaymentHostedService.SupportsLNURL(pp))
                return BadRequest();

            var boltcardUrl = Url.Action(nameof(UIBoltcardController.GetWithdrawRequest), "UIBoltcard");
            boltcardUrl = Request.GetAbsoluteUri(boltcardUrl);
            var websocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            var vaultClient = new VaultClient(websocket);
            var transport = new APDUVaultTransport(vaultClient);
            var ntag = new Ntag424(transport);
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
            {
next:
                while (websocket.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    try
                    {
                        var command = await vaultClient.GetNextCommand(cts.Token);
                        var permission = await vaultClient.AskPermission(VaultServices.NFC, cts.Token);
                        if (permission is null)
                        {
                            await vaultClient.Show(VaultMessageType.Error, StringLocalizer["BTCPay Server Vault does not seem to be running, you can download it on {0}.", new HtmlString("<a href=\"https://github.com/btcpayserver/BTCPayServer.Vault/releases/latest/\" class=\"alert-link\" target=\"_blank\" rel=\"noreferrer noopener\">GitHub</a>")], cts.Token);
                            goto next;
                        }
                        await vaultClient.Show(VaultMessageType.Ok, StringLocalizer["BTCPayServer successfully connected to the vault."], cts.Token);
                        if (permission is false)
                        {
                            await vaultClient.Show(VaultMessageType.Error, StringLocalizer["The user declined access to the vault."], cts.Token);
                            goto next;
                        }
                        await vaultClient.Show(VaultMessageType.Ok, StringLocalizer["Access to vault granted by owner."], cts.Token);

                        await vaultClient.Show(VaultMessageType.Processing, StringLocalizer["Waiting for NFC to be presented..."], cts.Token);
                        await transport.WaitForCard(cts.Token);
                        await vaultClient.Show(VaultMessageType.Ok, StringLocalizer["NFC detected."], cts.Token);

                        var issuerKey = await _settingsRepository.GetIssuerKey(_env);
                        CardOrigin cardOrigin = await GetCardOrigin(pullPaymentId, ntag, issuerKey, cts.Token);

                        if (cardOrigin is CardOrigin.OtherIssuer)
                        {
                            await vaultClient.Show(VaultMessageType.Error, StringLocalizer["This card is already configured for another issuer"], cts.Token);
                            goto next;
                        }

                        bool success = false;
                        switch (command)
                        {
                            case "configure-boltcard":
                                await vaultClient.Show(VaultMessageType.Processing, StringLocalizer["Configuring Boltcard..."], cts.Token);
                                if (cardOrigin is CardOrigin.Blank || cardOrigin is CardOrigin.ThisIssuerReset)
                                {
                                    await ntag.AuthenticateEV2First(0, AESKey.Default, cts.Token);
                                    var uid = await ntag.GetCardUID();
                                    try
                                    {
                                        var version = await _dbContextFactory.LinkBoltcardToPullPayment(pullPaymentId, issuerKey, uid);
                                        var cardKey = issuerKey.CreatePullPaymentCardKey(uid, version, pullPaymentId);
                                        await ntag.SetupBoltcard(boltcardUrl, BoltcardKeys.Default, cardKey.DeriveBoltcardKeys(issuerKey));
                                    }
                                    catch
                                    {
                                        await _dbContextFactory.SetBoltcardResetState(issuerKey, uid);
                                        throw;
                                    }
                                    await vaultClient.Show(VaultMessageType.Ok, StringLocalizer["The card is now configured"], cts.Token);
                                }
                                else if (cardOrigin is CardOrigin.ThisIssuer)
                                {
                                    await vaultClient.Show(VaultMessageType.Ok, StringLocalizer["This card is already properly configured"], cts.Token);
                                }
                                success = true;
                                break;
                            case "reset-boltcard":
                                await vaultClient.Show(VaultMessageType.Processing, StringLocalizer["Resetting Boltcard..."], cts.Token);
                                if (cardOrigin is CardOrigin.Blank)
                                {
                                    await vaultClient.Show(VaultMessageType.Ok, StringLocalizer["This card is already in a factory state"], cts.Token);
                                }
                                else if (cardOrigin is CardOrigin.ThisIssuer thisIssuer)
                                {
                                    var cardKey = issuerKey.CreatePullPaymentCardKey(thisIssuer.Registration.UId, thisIssuer.Registration.Version, pullPaymentId);
                                    await ntag.ResetCard(issuerKey, cardKey);
                                    await _dbContextFactory.SetBoltcardResetState(issuerKey, thisIssuer.Registration.UId);
                                    await vaultClient.Show(VaultMessageType.Ok, StringLocalizer["Card reset succeed"], cts.Token);
                                }
                                success = true;
                                break;
                        }
                        if (success)
                        {
                            await vaultClient.Show(VaultMessageType.Processing, StringLocalizer["Please remove the NFC from the card reader"], cts.Token);
                            await transport.WaitForRemoved(cts.Token);
                            await vaultClient.Show(VaultMessageType.Ok, StringLocalizer["Thank you!"], cts.Token);
                            await vaultClient.SendSimpleMessage("done", cts.Token);
                        }
                    }
                    catch (Exception) when (websocket.State != WebSocketState.Open || cts.IsCancellationRequested)
                    {
                        await WebsocketHelper.CloseSocket(websocket);
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            await vaultClient.Show(VaultMessageType.Error, StringLocalizer["Unexpected error: {0}", ex.Message], ex.ToString(), cts.Token);
                        }
                        catch { }
                    }
                }
            }
            return new EmptyResult();
        }

        private async Task<CardOrigin> GetCardOrigin(string pullPaymentId, Ntag424 ntag, IssuerKey issuerKey, CancellationToken cancellationToken)
        {
            CardOrigin cardOrigin;
            Uri uri = await ntag.TryReadNDefURI(cancellationToken);

            if (uri is null)
            {
                cardOrigin = new CardOrigin.Blank();
            }
            else
            {
                var piccData = issuerKey.TryDecrypt(uri);
                if (piccData is null)
                {
                    cardOrigin = new CardOrigin.OtherIssuer();
                }
                else
                {
                    var res = await _dbContextFactory.GetBoltcardRegistration(issuerKey, piccData.Uid);
                    if (res != null && res.PullPaymentId is null)
                        cardOrigin = new CardOrigin.ThisIssuerReset(res);
                    else if (res?.PullPaymentId != pullPaymentId)
                        cardOrigin = new CardOrigin.OtherIssuer();
                    else
                        cardOrigin = new CardOrigin.ThisIssuerConfigured(res.PullPaymentId, res);
                }
            }

            return cardOrigin;
        }
    }
}
