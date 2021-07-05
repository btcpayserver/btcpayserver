using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.WalletViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Notifications.Blobs;
using Microsoft.AspNetCore.Routing;
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

public class BitcoinLikePayoutHandler : IPayoutHandler
{
    private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
    private readonly ExplorerClientProvider _explorerClientProvider;
    private readonly BTCPayNetworkJsonSerializerSettings _jsonSerializerSettings;
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly EventAggregator _eventAggregator;
    private readonly NotificationSender _notificationSender;

    public BitcoinLikePayoutHandler(BTCPayNetworkProvider btcPayNetworkProvider,
        ExplorerClientProvider explorerClientProvider, BTCPayNetworkJsonSerializerSettings jsonSerializerSettings,
        ApplicationDbContextFactory dbContextFactory, EventAggregator eventAggregator, NotificationSender notificationSender)
    {
        _btcPayNetworkProvider = btcPayNetworkProvider;
        _explorerClientProvider = explorerClientProvider;
        _jsonSerializerSettings = jsonSerializerSettings;
        _dbContextFactory = dbContextFactory;
        _eventAggregator = eventAggregator;
        _notificationSender = notificationSender;
    }

    public bool CanHandle(PaymentMethodId paymentMethod)
    {
        return paymentMethod.PaymentType == BitcoinPaymentType.Instance &&
               _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethod.CryptoCode)?.ReadonlyWallet is false;
    }

    public async Task TrackClaim(PaymentMethodId paymentMethodId, IClaimDestination claimDestination)
    {
        var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
        var explorerClient = _explorerClientProvider.GetExplorerClient(network);
        if (claimDestination is IBitcoinLikeClaimDestination bitcoinLikeClaimDestination)
            await explorerClient.TrackAsync(TrackedSource.Create(bitcoinLikeClaimDestination.Address));
    }

    public Task<IClaimDestination> ParseClaimDestination(PaymentMethodId paymentMethodId, string destination)
    {
        var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
        destination = destination.Trim();
        try
        {
            if (destination.StartsWith($"{network.UriScheme}:", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<IClaimDestination>(new UriClaimDestination(new BitcoinUrlBuilder(destination, network.NBitcoinNetwork)));
            }

            return Task.FromResult<IClaimDestination>(new AddressClaimDestination(BitcoinAddress.Create(destination, network.NBitcoinNetwork)));
        }
        catch
        {
            return Task.FromResult<IClaimDestination>(null);
        }
    }

    public IPayoutProof ParseProof(PayoutData payout)
    {
        if (payout?.Proof is null)
            return null;
        var paymentMethodId = payout.GetPaymentMethodId();
        var raw =  JObject.Parse(Encoding.UTF8.GetString(payout.Proof));
        if (raw.TryGetValue("proofType", StringComparison.InvariantCultureIgnoreCase, out var proofType) &&
            proofType.Value<string>() == ManualPayoutProof.Type)
        {
            return raw.ToObject<ManualPayoutProof>();
        }
        var res = raw.ToObject<PayoutTransactionOnChainBlob>(
            JsonSerializer.Create(_jsonSerializerSettings.GetSerializer(paymentMethodId.CryptoCode)));
        var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
        if (res == null) return null;
        res.LinkTemplate = network.BlockExplorerLink;
        return res;
    }

    public void StartBackgroundCheck(Action<Type[]> subscribe)
    {
        subscribe(new[] {typeof(NewOnChainTransactionEvent), typeof(NewBlockEvent)});
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

    public Task<decimal> GetMinimumPayoutAmount(PaymentMethodId paymentMethodId, IClaimDestination claimDestination)
    {
        if (_btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode)?
                .NBitcoinNetwork?
                .Consensus?
                .ConsensusFactory?
                .CreateTxOut() is TxOut txout &&
            claimDestination is IBitcoinLikeClaimDestination bitcoinLikeClaimDestination)
        {
            txout.ScriptPubKey = bitcoinLikeClaimDestination.Address.ScriptPubKey;
            return Task.FromResult(txout.GetDustThreshold(new FeeRate(1.0m)).ToDecimal(MoneyUnit.BTC));
        }

        return Task.FromResult(0m);
    }


    public Dictionary<PayoutState, List<(string Action, string Text)>> GetPayoutSpecificActions()
    {
        return new Dictionary<PayoutState, List<(string Action, string Text)>>()
        {
            {PayoutState.AwaitingPayment, new List<(string Action, string Text)>()
            {
                ("confirm-payment", "Confirm payouts as paid"),
                ("reject-payment", "Reject payout transaction")
            }}
        };
    }

    public async Task<StatusMessageModel> DoSpecificAction(string action, string[] payoutIds, string storeId)
    {
        switch (action)
        {
            case "confirm-payment":
                await using (var context = _dbContextFactory.CreateContext())
                {
                    var payouts = (await context.Payouts
                            .Include(p => p.PullPaymentData)
                            .Include(p => p.PullPaymentData.StoreData)
                            .Where(p => payoutIds.Contains(p.Id))
                            .Where(p => p.PullPaymentData.StoreId == storeId && !p.PullPaymentData.Archived && p.State == PayoutState.AwaitingPayment)
                            .ToListAsync()).Where(data => CanHandle(PaymentMethodId.Parse(data.PaymentMethodId)))
                        .Select(data => (data, ParseProof(data) as PayoutTransactionOnChainBlob)).Where(tuple=> tuple.Item2 != null && tuple.Item2.TransactionId != null && tuple.Item2.Accounted == false);
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
                    var payouts = (await context.Payouts
                            .Include(p => p.PullPaymentData)
                            .Include(p => p.PullPaymentData.StoreData)
                            .Where(p => payoutIds.Contains(p.Id))
                            .Where(p => p.PullPaymentData.StoreId == storeId && !p.PullPaymentData.Archived && p.State == PayoutState.AwaitingPayment)
                            .ToListAsync()).Where(data => CanHandle(PaymentMethodId.Parse(data.PaymentMethodId)))
                        .Select(data => (data, ParseProof(data) as PayoutTransactionOnChainBlob)).Where(tuple=> tuple.Item2 != null && tuple.Item2.TransactionId != null && tuple.Item2.Accounted == true);
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

        return new StatusMessageModel()
        {
            Message = "Unknown action",
            Severity = StatusMessageModel.StatusSeverity.Error
        };;
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
                    var explorer = _explorerClientProvider.GetExplorerClient(payout.GetPaymentMethodId().CryptoCode);
                    var tx = await explorer.GetTransactionAsync(txid);
                    if (tx is null)
                    {
                        proof.Candidates.Remove(txid);
                    }
                    else if (tx.Confirmations >= payoutBlob.MinimumConfirmation)
                    {
                        payout.State = PayoutState.Completed;
                        proof.TransactionId = tx.TransactionHash;
                        payout.Destination = null;
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
                            continue;
                        }
                    }
                }

                if (proof.TransactionId is null && !proof.Candidates.Contains(proof.TransactionId))
                {
                    proof.TransactionId = null;
                }

                if (proof.Candidates.Count == 0)
                {
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
            Dictionary<string, decimal> destinations = new Dictionary<string, decimal>
            {
                {
                    addressTrackedSource.Address.ToString(),
                    newTransaction.NewTransactionEvent.Outputs.Sum(output => output.Value.GetValue(network))
                }
            };
            var paymentMethodId = new PaymentMethodId(newTransaction.CryptoCode, BitcoinPaymentType.Instance);

            await using var ctx = _dbContextFactory.CreateContext();
            var payouts = await ctx.Payouts
                .Include(o => o.PullPaymentData)
                .ThenInclude(o => o.StoreData)
                .Where(p => p.State == PayoutState.AwaitingPayment)
                .Where(p => p.PaymentMethodId == paymentMethodId.ToString())
                .Where(p => destinations.Keys.Contains(p.Destination))
                .ToListAsync();
            var payoutByDestination = payouts.ToDictionary(p => p.Destination);
            foreach (var destination in destinations)
            {
                if (!payoutByDestination.TryGetValue(destination.Key, out var payout))
                    continue;
                var payoutBlob = payout.GetBlob(_jsonSerializerSettings);
                if (payoutBlob.CryptoAmount is null ||
                    // The round up here is not strictly necessary, this is temporary to fix existing payout before we
                    // were properly roundup the crypto amount
                    destination.Value != BTCPayServer.Extensions.RoundUp(payoutBlob.CryptoAmount.Value, network.Divisibility))
                    continue;

                var derivationSchemeSettings = payout.PullPaymentData.StoreData
                    .GetDerivationSchemeSettings(_btcPayNetworkProvider, newTransaction.CryptoCode).AccountDerivation;

                var storeWalletMatched = (await _explorerClientProvider.GetExplorerClient(newTransaction.CryptoCode)
                    .GetTransactionAsync(derivationSchemeSettings,
                        newTransaction.NewTransactionEvent.TransactionData.TransactionHash));
                //if the wallet related to the store related to the payout does not have the tx: it is external
                //if the wallet has the tx but none of the inputs or outputs that matched weren't the payout's output: it is external 
                var isInternal = storeWalletMatched is null? false:  !newTransaction.NewTransactionEvent.Outputs.All(output => storeWalletMatched.Outputs.Any(
                    matchedOutput => matchedOutput.Index == output.Index && matchedOutput.Value == output.Value &&
                                     matchedOutput.ScriptPubKey == output.ScriptPubKey) ) && !newTransaction.NewTransactionEvent.Outputs.All(output => storeWalletMatched.Inputs.Any(
                    matchedInput => matchedInput.Index == output.Index && matchedInput.Value == output.Value &&
                                     matchedInput.ScriptPubKey == output.ScriptPubKey) );
                var proof = ParseProof(payout) as PayoutTransactionOnChainBlob ?? new PayoutTransactionOnChainBlob()
                {
                    Accounted = isInternal
                };
                var txId = newTransaction.NewTransactionEvent.TransactionData.TransactionHash;
                if (!proof.Candidates.Add(txId)) continue;
                if (isInternal)
                {
                    payout.State = PayoutState.InProgress;
                    var walletId = new WalletId(payout.PullPaymentData.StoreId, newTransaction.CryptoCode);
                    _eventAggregator.Publish(new UpdateTransactionLabel(walletId,	
                        newTransaction.NewTransactionEvent.TransactionData.TransactionHash,	
                        UpdateTransactionLabel.PayoutTemplate(payout.Id,payout.PullPaymentDataId, walletId.ToString())));	
                }
                else
                {
                    await _notificationSender.SendNotification(new StoreScope(payout.PullPaymentData.StoreId), new ExternalPayoutTransactionNotification()
                    {
                        PaymentMethod = payout.PaymentMethodId,
                        PayoutId = payout.Id,
                        StoreId = payout.PullPaymentData.StoreId
                    });
                }
                proof.TransactionId ??= txId;
                SetProofBlob(payout, proof);
            }

            await ctx.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Logs.PayServer.LogWarning(ex, "Error while processing a transaction in the pull payment hosted service");
        }
    }
    
    private void SetProofBlob(PayoutData data, PayoutTransactionOnChainBlob blob)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(blob, _jsonSerializerSettings.GetSerializer(data.GetPaymentMethodId().CryptoCode)));
        // We only update the property if the bytes actually changed, this prevent from hammering the DB too much
        if (data.Proof is null || bytes.Length != data.Proof.Length || !bytes.SequenceEqual(data.Proof))
        {
            data.Proof = bytes;
        }
    }
}
