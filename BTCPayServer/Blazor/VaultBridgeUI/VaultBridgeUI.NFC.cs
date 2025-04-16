using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Hwi;
using BTCPayServer.NTag424;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NBitcoin;
using static BTCPayServer.Blazor.VaultBridgeUI.VaultElement.Feedback;
using static BTCPayServer.BoltcardDataExtensions;

namespace BTCPayServer.Blazor;

public partial class VaultBridgeUI
{
    public class NFCParameters
    {
        public bool NewCard { get; set; }
        public string PullPaymentId { get; set; }
        public string BoltcardUrl { get; set; }
    }

    [Parameter] public NFCParameters NFC { get; set; }

    record CardOrigin
    {
        public record Blank() : CardOrigin;

        public record ThisIssuer(BoltcardRegistration Registration) : CardOrigin;

        public record ThisIssuerConfigured(string PullPaymentId, BoltcardRegistration Registration) : ThisIssuer(Registration);

        public record OtherIssuer() : CardOrigin;

        public record ThisIssuerReset(BoltcardRegistration Registration) : ThisIssuer(Registration);
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
                var dbContextFactory = ServiceProvider.GetRequiredService<ApplicationDbContextFactory>();
                var res = await dbContextFactory.GetBoltcardRegistration(issuerKey, piccData.Uid);
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

    public async Task ConnectToNFC(VaultClient client)
    {
        try
        {
            this.ShowFeedback(StateValue.Loading, StringLocalizer["Waiting for NFC to be presented..."]);
            var transport = new APDUVaultTransport(client);
            var ntag = new Ntag424(transport);

            await transport.WaitForCard(CancellationToken);
            this.ShowFeedback(StateValue.Success, StringLocalizer["NFC detected."]);

            var settingsRepository = ServiceProvider.GetRequiredService<SettingsRepository>();
            var env = ServiceProvider.GetRequiredService<BTCPayServerEnvironment>();
            var issuerKey = await settingsRepository.GetIssuerKey(env);
            var dbContextFactory = ServiceProvider.GetRequiredService<ApplicationDbContextFactory>();
            CardOrigin cardOrigin = await GetCardOrigin(NFC.PullPaymentId, ntag, issuerKey, CancellationToken);

            if (cardOrigin is CardOrigin.OtherIssuer)
            {
                this.ShowFeedback(StateValue.Failed, StringLocalizer["This card is already configured for another issuer"]);
                ShowRetry();
                return;
            }

            if (NFC.NewCard)
            {
                this.ShowFeedback(StateValue.Loading, StringLocalizer["Configuring Boltcard..."]);
                if (cardOrigin is CardOrigin.Blank || cardOrigin is CardOrigin.ThisIssuerReset)
                {
                    await ntag.AuthenticateEV2First(0, AESKey.Default, CancellationToken);
                    var uid = await ntag.GetCardUID(CancellationToken);
                    try
                    {
                        var version = await dbContextFactory.LinkBoltcardToPullPayment(NFC.PullPaymentId, issuerKey, uid);
                        var cardKey = issuerKey.CreatePullPaymentCardKey(uid, version, NFC.PullPaymentId);
                        await ntag.SetupBoltcard(NFC.BoltcardUrl, BoltcardKeys.Default, cardKey.DeriveBoltcardKeys(issuerKey));
                    }
                    catch
                    {
                        await dbContextFactory.SetBoltcardResetState(issuerKey, uid);
                        throw;
                    }

                    this.ShowFeedback(StateValue.Success, StringLocalizer["The card is now configured"]);
                }
                else if (cardOrigin is CardOrigin.ThisIssuer)
                {
                    this.ShowFeedback(StateValue.Success, StringLocalizer["This card is already properly configured"]);
                }
            }
            else
            {
                this.ShowFeedback(StateValue.Loading, StringLocalizer["Resetting Boltcard..."]);
                if (cardOrigin is CardOrigin.Blank)
                {
                    this.ShowFeedback(StateValue.Success, StringLocalizer["This card is already in a factory state"]);
                }
                else if (cardOrigin is CardOrigin.ThisIssuer thisIssuer)
                {
                    var cardKey = issuerKey.CreatePullPaymentCardKey(thisIssuer.Registration.UId, thisIssuer.Registration.Version, NFC.PullPaymentId);
                    await ntag.ResetCard(issuerKey, cardKey);
                    await dbContextFactory.SetBoltcardResetState(issuerKey, thisIssuer.Registration.UId);
                    this.ShowFeedback(StateValue.Success, StringLocalizer["Card reset succeed"]);
                }
            }

            this.ShowFeedback(StateValue.Loading, StringLocalizer["Please remove the NFC from the card reader"]);
            await transport.WaitForRemoved(CancellationToken);
            this.ShowFeedback(StateValue.Success, StringLocalizer["Thank you!"]);
        }
        catch (UnexpectedResponseException e)
        {
            this.ShowFeedback(StateValue.Failed, StringLocalizer["An unexpected error happened: {0}", e.Message]);
            this.ShowRetry();
            return;
        }

        // Give them time to read the message
        await Task.Delay(1000, CancellationToken);
        await this.JSRuntime.InvokeVoidAsync("vault.done", CancellationToken);
    }
}
