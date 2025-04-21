using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Blazor.VaultBridge.Elements;
using BTCPayServer.Data;
using BTCPayServer.NTag424;
using BTCPayServer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using static BTCPayServer.BoltcardDataExtensions;

namespace BTCPayServer.Blazor.VaultBridge;

record CardOrigin
{
    public record Blank() : CardOrigin;

    public record ThisIssuer(BoltcardRegistration Registration) : CardOrigin;

    public record ThisIssuerConfigured(string PullPaymentId, BoltcardRegistration Registration) : ThisIssuer(Registration);

    public record OtherIssuer() : CardOrigin;

    public record ThisIssuerReset(BoltcardRegistration Registration) : ThisIssuer(Registration);
}

public class NFCController : VaultController
{
    protected override string VaultUri => "http://127.0.0.1:65092/nfc-bridge/v1";
    public bool NewCard { get; set; }
    public string PullPaymentId { get; set; }
    public string BoltcardUrl { get; set; }

    protected override async Task Run(VaultBridgeUI ui, VaultClient vaultClient, CancellationToken cancellationToken)
    {
        try
        {
            ui.ShowFeedback(FeedbackType.Loading, ui.StringLocalizer["Waiting for NFC to be presented..."]);
            var transport = new APDUVaultTransport(vaultClient);
            var ntag = new Ntag424(transport);

            await transport.WaitForCard(cancellationToken);
            ui.ShowFeedback(FeedbackType.Success, ui.StringLocalizer["NFC detected."]);

            var settingsRepository = ui.ServiceProvider.GetRequiredService<SettingsRepository>();
            var env = ui.ServiceProvider.GetRequiredService<BTCPayServerEnvironment>();
            var issuerKey = await settingsRepository.GetIssuerKey(env);
            var dbContextFactory = ui.ServiceProvider.GetRequiredService<ApplicationDbContextFactory>();
            CardOrigin cardOrigin = await GetCardOrigin(dbContextFactory, PullPaymentId, ntag, issuerKey, cancellationToken);

            if (cardOrigin is CardOrigin.OtherIssuer)
            {
                ui.ShowFeedback(FeedbackType.Failed, ui.StringLocalizer["This card is already configured for another issuer"]);
                ui.ShowRetry();
                return;
            }

            if (NewCard)
            {
                ui.ShowFeedback(FeedbackType.Loading, ui.StringLocalizer["Configuring Boltcard..."]);
                if (cardOrigin is CardOrigin.Blank || cardOrigin is CardOrigin.ThisIssuerReset)
                {
                    await ntag.AuthenticateEV2First(0, AESKey.Default, cancellationToken);
                    var uid = await ntag.GetCardUID(cancellationToken);
                    try
                    {
                        var version = await dbContextFactory.LinkBoltcardToPullPayment(PullPaymentId, issuerKey, uid);
                        var cardKey = issuerKey.CreatePullPaymentCardKey(uid, version, PullPaymentId);
                        await ntag.SetupBoltcard(BoltcardUrl, BoltcardKeys.Default, cardKey.DeriveBoltcardKeys(issuerKey));
                    }
                    catch
                    {
                        await dbContextFactory.SetBoltcardResetState(issuerKey, uid);
                        throw;
                    }

                    ui.ShowFeedback(FeedbackType.Success, ui.StringLocalizer["The card is now configured"]);
                }
                else if (cardOrigin is CardOrigin.ThisIssuer)
                {
                    ui.ShowFeedback(FeedbackType.Success, ui.StringLocalizer["This card is already properly configured"]);
                }
            }
            else
            {
                ui.ShowFeedback(FeedbackType.Loading, ui.StringLocalizer["Resetting Boltcard..."]);
                if (cardOrigin is CardOrigin.Blank)
                {
                    ui.ShowFeedback(FeedbackType.Success, ui.StringLocalizer["This card is already in a factory state"]);
                }
                else if (cardOrigin is CardOrigin.ThisIssuer thisIssuer)
                {
                    var cardKey = issuerKey.CreatePullPaymentCardKey(thisIssuer.Registration.UId, thisIssuer.Registration.Version, PullPaymentId);
                    await ntag.ResetCard(issuerKey, cardKey);
                    await dbContextFactory.SetBoltcardResetState(issuerKey, thisIssuer.Registration.UId);
                    ui.ShowFeedback(FeedbackType.Success, ui.StringLocalizer["Card reset succeed"]);
                }
            }

            ui.ShowFeedback(FeedbackType.Loading, ui.StringLocalizer["Please remove the NFC from the card reader"]);
            await transport.WaitForRemoved(cancellationToken);
            ui.ShowFeedback(FeedbackType.Success, ui.StringLocalizer["Thank you!"]);
        }
        catch (UnexpectedResponseException e)
        {
            ui.ShowFeedback(FeedbackType.Failed, ui.StringLocalizer["An unexpected error happened: {0}", e.Message]);
            ui.ShowRetry();
            return;
        }

        // Give them time to read the message
        await Task.Delay(1000, cancellationToken);
        await ui.JSRuntime.InvokeVoidAsync("vault.done", cancellationToken);
    }

    private async Task<CardOrigin> GetCardOrigin(ApplicationDbContextFactory dbContextFactory, string pullPaymentId, Ntag424 ntag, IssuerKey issuerKey,
        CancellationToken cancellationToken)
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
}
