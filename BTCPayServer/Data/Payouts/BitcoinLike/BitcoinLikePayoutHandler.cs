using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payouts;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Notifications.Blobs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Payment;
using NBitcoin.RPC;
using NBXplorer.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NewBlockEvent = BTCPayServer.Events.NewBlockEvent;
using PayoutData = BTCPayServer.Data.PayoutData;
using StoreData = BTCPayServer.Data.StoreData;

public class BitcoinLikePayoutHandler : IPayoutHandler, IHasNetwork
{
    public string Currency { get; }
    private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
    private readonly PaymentMethodHandlerDictionary _paymentHandlers;
    private readonly ExplorerClientProvider _explorerClientProvider;
    private readonly BTCPayNetworkJsonSerializerSettings _jsonSerializerSettings;
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly NotificationSender _notificationSender;
    private readonly Logs Logs;
    private readonly EventAggregator _eventAggregator;
    private readonly TransactionLinkProviders _transactionLinkProviders;

    PayoutMethodId IHandler<PayoutMethodId>.Id => PayoutMethodId;
    public PayoutMethodId PayoutMethodId { get; }
    public PaymentMethodId PaymentMethodId { get; }
    public BTCPayNetwork Network { get; }
    public string[] DefaultRateRules => Network.DefaultRateRules;
    public WalletRepository WalletRepository { get; }

    public BitcoinLikePayoutHandler(BTCPayNetworkProvider btcPayNetworkProvider,
        PayoutMethodId payoutMethodId,
        BTCPayNetwork network,
        PaymentMethodHandlerDictionary handlers,
        WalletRepository walletRepository,
        ExplorerClientProvider explorerClientProvider,
        BTCPayNetworkJsonSerializerSettings jsonSerializerSettings,
        ApplicationDbContextFactory dbContextFactory,
        NotificationSender notificationSender,
        Logs logs,
        EventAggregator eventAggregator,
        TransactionLinkProviders transactionLinkProviders)
    {
        _btcPayNetworkProvider = btcPayNetworkProvider;
        PayoutMethodId = payoutMethodId;
        PaymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
        Network = network;
        _paymentHandlers = handlers;
        WalletRepository = walletRepository;
        _explorerClientProvider = explorerClientProvider;
        _jsonSerializerSettings = jsonSerializerSettings;
        _dbContextFactory = dbContextFactory;
        _notificationSender = notificationSender;
        Currency = network.CryptoCode;
        this.Logs = logs;
        _eventAggregator = eventAggregator;
        _transactionLinkProviders = transactionLinkProviders;
    }

    public async Task TrackClaim(ClaimRequest claimRequest, PayoutData payoutData)
    {
        var explorerClient = _explorerClientProvider.GetExplorerClient(Network);
        if (claimRequest.Destination is IBitcoinLikeClaimDestination bitcoinLikeClaimDestination)
        {

            await explorerClient.TrackAsync(TrackedSource.Create(bitcoinLikeClaimDestination.Address));
            await WalletRepository.AddWalletTransactionAttachment(
                new WalletId(claimRequest.StoreId, Network.CryptoCode),
                bitcoinLikeClaimDestination.Address.ToString(),
                Attachment.Payout(payoutData.PullPaymentDataId, payoutData.Id), WalletObjectData.Types.Address);
        }
    }

    public Task<(IClaimDestination destination, string error)> ParseClaimDestination(string destination, CancellationToken cancellationToken)
    {
        destination = destination.Trim();
        try
        {
            if (destination.StartsWith($"{Network.NBitcoinNetwork.UriScheme}:", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<(IClaimDestination, string)>((new UriClaimDestination(new BitcoinUrlBuilder(destination, Network.NBitcoinNetwork)), null));
            }

            return Task.FromResult<(IClaimDestination, string)>((new AddressClaimDestination(BitcoinAddress.Create(destination, Network.NBitcoinNetwork)), null));
        }
        catch
        {
            return Task.FromResult<(IClaimDestination, string)>(
                (null, "A valid address was not provided"));
        }
    }

    public (bool valid, string error) ValidateClaimDestination(IClaimDestination claimDestination, PullPaymentBlob pullPaymentBlob)
    {
        return (true, null);
    }

    public IPayoutProof ParseProof(PayoutData payout)
    {
        if (payout?.Proof is null)
            return null;
        var payoutMethodId = payout.GetPayoutMethodId();
        if (payoutMethodId is null)
            return null;
        var cryptoCode = Network.CryptoCode;
        ParseProofType(payout.Proof, out var raw, out var proofType);
        if (proofType == PayoutTransactionOnChainBlob.Type)
        {

            var res = raw.ToObject<PayoutTransactionOnChainBlob>(
                JsonSerializer.Create(_jsonSerializerSettings.GetSerializer(payoutMethodId)));
            if (res == null)
                return null;
            res.LinkTemplate = _transactionLinkProviders.GetBlockExplorerLink(cryptoCode);
            return res;
        }
        return raw.ToObject<ManualPayoutProof>();
    }

    public static void ParseProofType(string proof, out JObject obj, out string type)
    {
        type = null;
        if (proof is null)
        {
            obj = null;
            return;
        }

        obj = JObject.Parse(proof);
        TryParseProofType(obj, out type);
    }

    public static bool TryParseProofType(JObject proof, out string type)
    {
        type = null;
        if (proof is null)
        {
            return false;
        }

        if (!proof.TryGetValue("proofType", StringComparison.InvariantCultureIgnoreCase, out var proofType))
            return false;
        type = proofType.Value<string>();
        return true;
    }

    public void StartBackgroundCheck(Action<Type[]> subscribe)
    {
        subscribe(new[] { typeof(NewOnChainTransactionEvent), typeof(NewBlockEvent) });
    }

    public async Task BackgroundCheck(object o)
    {
        if (o is NewOnChainTransactionEvent newTransaction && newTransaction.NewTransactionEvent.TrackedSource is AddressTrackedSource addressTrackedSource)
        {
            await UpdatePayoutsAwaitingForPayment(newTransaction, addressTrackedSource);
        }

        if (o is NewBlockEvent || o is NewOnChainTransactionEvent)
        {
            await UpdatePayoutsInProgress();
        }
    }

    public Task<decimal> GetMinimumPayoutAmount(IClaimDestination claimDestination)
    {
        if (Network
                .NBitcoinNetwork?
                .Consensus?
                .ConsensusFactory?
                .CreateTxOut() is TxOut txout &&
            claimDestination is IBitcoinLikeClaimDestination bitcoinLikeClaimDestination)
        {
            txout.ScriptPubKey = bitcoinLikeClaimDestination.Address.ScriptPubKey;
            return Task.FromResult(txout.GetDustThreshold().ToDecimal(MoneyUnit.BTC));
        }

        return Task.FromResult(0m);
    }


    public Dictionary<PayoutState, List<(string Action, string Text)>> GetPayoutSpecificActions()
    {
        return new Dictionary<PayoutState, List<(string Action, string Text)>>()
        {
            {PayoutState.AwaitingPayment, new List<(string Action, string Text)>()
            {
                ("reject-payment", "Reject payout transaction")
            }}
        };
    }

    public async Task<StatusMessageModel> DoSpecificAction(string action, string[] payoutIds, string storeId)
    {
        switch (action)
        {
            case "mark-paid":
                await using (var context = _dbContextFactory.CreateContext())
                {
                    var payouts = (await PullPaymentHostedService.GetPayouts(new PullPaymentHostedService.PayoutQuery()
                    {
                        States = new[] { PayoutState.AwaitingPayment },
                        Stores = new[] { storeId },
                        PayoutIds = payoutIds
                    }, context)).Where(data =>
                        PayoutMethodId.TryParse(data.PayoutMethodId, out var payoutMethodId) &&
                        payoutMethodId == PayoutMethodId)
                        .Select(data => (data, ParseProof(data) as PayoutTransactionOnChainBlob)).Where(tuple => tuple.Item2 != null && tuple.Item2.TransactionId != null && tuple.Item2.Accounted == false);
                    foreach (var valueTuple in payouts)
                    {
                        valueTuple.Item2.Accounted = true;
                        valueTuple.data.State = PayoutState.InProgress;
                        SetProofBlob(valueTuple.data, valueTuple.Item2);
                    }
                    await context.SaveChangesAsync();
                }
                return new StatusMessageModel()
                {
                    Message = "Payout payments have been marked confirmed",
                    Severity = StatusMessageModel.StatusSeverity.Success
                };
            case "reject-payment":
                await using (var context = _dbContextFactory.CreateContext())
                {
                    var payouts = (await PullPaymentHostedService.GetPayouts(new PullPaymentHostedService.PayoutQuery()
                    {
                        States = new[] { PayoutState.AwaitingPayment },
                        Stores = new[] { storeId },
                        PayoutIds = payoutIds
                    }, context)).Where(data =>
                        PayoutMethodId.TryParse(data.PayoutMethodId, out var payoutMethodId) &&
                        payoutMethodId == PayoutMethodId)
                        .Select(data => (data, ParseProof(data) as PayoutTransactionOnChainBlob)).Where(tuple => tuple.Item2 != null && tuple.Item2.TransactionId != null && tuple.Item2.Accounted == true);
                    foreach (var valueTuple in payouts)
                    {
                        valueTuple.Item2.TransactionId = null;
                        SetProofBlob(valueTuple.data, valueTuple.Item2);
                    }

                    await context.SaveChangesAsync();
                }

                return new StatusMessageModel()
                {
                    Message = "Payout payments have been unmarked",
                    Severity = StatusMessageModel.StatusSeverity.Success
                };
        }

        return null;
    }

    public bool IsSupported(StoreData storeData)
    {
        return storeData.GetDerivationSchemeSettings(_paymentHandlers, Network.CryptoCode, true)?.AccountDerivation is not null;
    }

    public async Task<IActionResult> InitiatePayment(string[] payoutIds)
    {
        await using var ctx = this._dbContextFactory.CreateContext();
        ctx.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        var payouts = await ctx.Payouts.Include(data => data.PullPaymentData)
            .Where(data => payoutIds.Contains(data.Id)
                           && PayoutMethodId.ToString() == data.PayoutMethodId
                           && data.State == PayoutState.AwaitingPayment)
            .ToListAsync();

        var pullPaymentIds = payouts.Select(data => data.PullPaymentDataId).Distinct().Where(s => s != null).ToArray();
        var storeId = payouts.First().StoreDataId;
        List<string> bip21 = new List<string>();
        foreach (var payout in payouts)
        {
            if (payout.Proof != null)
            {
                continue;
            }
            var blob = payout.GetBlob(_jsonSerializerSettings);
            if (payout.GetPayoutMethodId() != PayoutMethodId)
                continue;
            var claim = await ParseClaimDestination(blob.Destination, default);
            switch (claim.destination)
            {
                case UriClaimDestination uriClaimDestination:
                    uriClaimDestination.BitcoinUrl.Amount = new Money(payout.Amount.Value, MoneyUnit.BTC);
                    var newUri = new UriBuilder(uriClaimDestination.BitcoinUrl.Uri);
                    BTCPayServerClient.AppendPayloadToQuery(newUri, new KeyValuePair<string, object>("payout", payout.Id));
                    bip21.Add(newUri.Uri.ToString());
                    break;
                case AddressClaimDestination addressClaimDestination:
                    var bip21New = Network.GenerateBIP21(addressClaimDestination.Address.ToString(), payout.Amount.Value);
                    bip21New.QueryParams.Add("payout", payout.Id);
                    bip21.Add(bip21New.ToString());
                    break;
            }
        }
        if (bip21.Any())
            return new RedirectToActionResult("WalletSend", "UIWallets", new { walletId = new WalletId(storeId, Network.CryptoCode).ToString(), bip21 });
        return new RedirectToActionResult("Payouts", "UIWallets", new
        {
            walletId = new WalletId(storeId, Network.CryptoCode).ToString(),
            pullPaymentId = pullPaymentIds.Length == 1 ? pullPaymentIds.First() : null
        });
    }

    private async Task UpdatePayoutsInProgress()
    {
        try
        {
            await using var ctx = _dbContextFactory.CreateContext();
            var payouts = await ctx.Payouts
                .Include(p => p.PullPaymentData)
                .Where(p => p.State == PayoutState.InProgress)
                .ToListAsync();
            List<PayoutData> updatedPayouts = new List<PayoutData>();
            foreach (var payout in payouts)
            {
                var proof = ParseProof(payout) as PayoutTransactionOnChainBlob;
                var payoutBlob = payout.GetBlob(this._jsonSerializerSettings);
                if (proof is null || proof.Accounted is false)
                {
                    continue;
                }
                foreach (var txid in proof.Candidates.ToList())
                {
                    var explorer = _explorerClientProvider.GetExplorerClient(Network.CryptoCode);
                    var tx = await explorer.GetTransactionAsync(txid);
                    if (tx is null)
                    {
                        proof.Candidates.Remove(txid);
                    }
                    else if (tx.Confirmations >= payoutBlob.MinimumConfirmation)
                    {
                        payout.State = PayoutState.Completed;
                        proof.TransactionId = tx.TransactionHash;
                        updatedPayouts.Add(payout);
                        break;
                    }
                    else
                    {
                        var rebroadcasted = await explorer.BroadcastAsync(tx.Transaction);
                        if (rebroadcasted.RPCCode == RPCErrorCode.RPC_TRANSACTION_ERROR ||
                            rebroadcasted.RPCCode == RPCErrorCode.RPC_TRANSACTION_REJECTED)
                        {
                            proof.Candidates.Remove(txid);
                        }
                        else
                        {
                            payout.State = PayoutState.InProgress;
                            proof.TransactionId = tx.TransactionHash;
                            updatedPayouts.Add(payout);
                            continue;
                        }
                    }
                }

                if (proof.TransactionId is not null && !proof.Candidates.Contains(proof.TransactionId))
                {
                    proof.TransactionId = null;
                }

                if (proof.Candidates.Count == 0)
                {
                    if (payout.State != PayoutState.AwaitingPayment)
                    {
                        updatedPayouts.Add(payout);
                    }
                    payout.State = PayoutState.AwaitingPayment;
                }
                else if (proof.TransactionId is null)
                {
                    proof.TransactionId = proof.Candidates.First();
                }

                if (payout.State == PayoutState.Completed)
                    proof.Candidates = null;
                SetProofBlob(payout, proof);
            }

            await ctx.SaveChangesAsync();
            foreach (PayoutData payoutData in updatedPayouts)
            {
                _eventAggregator.Publish(new PayoutEvent(PayoutEvent.PayoutEventType.Updated,payoutData));
            }
        }
        catch (Exception ex)
        {
            Logs.PayServer.LogWarning(ex, "Error while processing an update in the pull payment hosted service");
        }
    }

    private async Task UpdatePayoutsAwaitingForPayment(NewOnChainTransactionEvent newTransaction,
        AddressTrackedSource addressTrackedSource)
    {
        try
        {
            var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(newTransaction.CryptoCode);
            var destinationSum =
                newTransaction.NewTransactionEvent.Outputs.Sum(output => output.Value.GetValue(network));
            var destination = addressTrackedSource.Address.ToString();
            var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(newTransaction.CryptoCode);

            await using var ctx = _dbContextFactory.CreateContext();
            var payout = await ctx.Payouts
                .Include(o => o.StoreData)
                .Include(o => o.PullPaymentData)
                .Where(p => p.State == PayoutState.AwaitingPayment)
                .Where(p => p.PayoutMethodId == paymentMethodId.ToString())
#pragma warning disable CA1307 // Specify StringComparison
                .Where(p => destination.Equals(p.DedupId))
#pragma warning restore CA1307 // Specify StringComparison
                .FirstOrDefaultAsync();

            if (payout is null)
                return;
            var payoutBlob = payout.GetBlob(_jsonSerializerSettings);
            if (payout.Amount is null ||
                // The round up here is not strictly necessary, this is temporary to fix existing payout before we
                // were properly roundup the crypto amount
                destinationSum !=
                BTCPayServer.Extensions.RoundUp(payout.Amount.Value, network.Divisibility))
                return;

            var derivationSchemeSettings = payout.StoreData
                .GetDerivationSchemeSettings(_paymentHandlers, newTransaction.CryptoCode)?.AccountDerivation;
            if (derivationSchemeSettings is null)
                return;

            var storeWalletMatched = (await _explorerClientProvider.GetExplorerClient(newTransaction.CryptoCode)
                .GetTransactionAsync(derivationSchemeSettings,
                    newTransaction.NewTransactionEvent.TransactionData.TransactionHash));
            //if the wallet related to the store of the payout does not have the tx: it has been paid externally
            var isInternal = storeWalletMatched is not null;

            var proof = ParseProof(payout) as PayoutTransactionOnChainBlob ??
                        new PayoutTransactionOnChainBlob() { Accounted = isInternal };
            var txId = newTransaction.NewTransactionEvent.TransactionData.TransactionHash;
            if (!proof.Candidates.Add(txId))
                return;
            if (isInternal)
            {
                payout.State = PayoutState.InProgress;
                await WalletRepository.AddWalletTransactionAttachment(
                    new WalletId(payout.StoreDataId, newTransaction.CryptoCode),
                    newTransaction.NewTransactionEvent.TransactionData.TransactionHash,
                    Attachment.Payout(payout.PullPaymentDataId, payout.Id));
            }
            else
            {
                await _notificationSender.SendNotification(new StoreScope(payout.StoreDataId),
                    new ExternalPayoutTransactionNotification()
                    {
                        PaymentMethod = payout.PayoutMethodId,
                        PayoutId = payout.Id,
                        StoreId = payout.StoreDataId
                    });
            }

            proof.TransactionId ??= txId;
            SetProofBlob(payout, proof);
            await ctx.SaveChangesAsync();
            _eventAggregator.Publish(new PayoutEvent(PayoutEvent.PayoutEventType.Updated,payout));
        }
        catch (Exception ex)
        {
            Logs.PayServer.LogWarning(ex, "Error while processing a transaction in the pull payment hosted service");
        }
    }

    public void SetProofBlob(PayoutData data, PayoutTransactionOnChainBlob blob)
    {
        data.SetProofBlob(blob, _jsonSerializerSettings.GetSerializer(data.GetPayoutMethodId()));

    }
}
