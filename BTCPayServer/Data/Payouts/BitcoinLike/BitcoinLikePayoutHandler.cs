using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Payment;
using NBitcoin.RPC;
using NBXplorer.Models;
using Newtonsoft.Json;
using NewBlockEvent = BTCPayServer.Events.NewBlockEvent;
using PayoutData = BTCPayServer.Data.PayoutData;

public class BitcoinLikePayoutHandler : IPayoutHandler
{
    private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
    private readonly ExplorerClientProvider _explorerClientProvider;
    private readonly BTCPayNetworkJsonSerializerSettings _jsonSerializerSettings;
    private readonly ApplicationDbContextFactory _dbContextFactory;
    private readonly EventAggregator _eventAggregator;

    public BitcoinLikePayoutHandler(BTCPayNetworkProvider btcPayNetworkProvider,
        ExplorerClientProvider explorerClientProvider, BTCPayNetworkJsonSerializerSettings jsonSerializerSettings,
        ApplicationDbContextFactory dbContextFactory, EventAggregator eventAggregator)
    {
        _btcPayNetworkProvider = btcPayNetworkProvider;
        _explorerClientProvider = explorerClientProvider;
        _jsonSerializerSettings = jsonSerializerSettings;
        _dbContextFactory = dbContextFactory;
        _eventAggregator = eventAggregator;
    }

    public bool CanHandle(PaymentMethodId paymentMethod)
    {
        return paymentMethod.PaymentType == BitcoinPaymentType.Instance &&
               _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethod.CryptoCode)?.ReadonlyWallet is false;
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
        var res =  JsonConvert.DeserializeObject<PayoutTransactionOnChainBlob>(Encoding.UTF8.GetString(payout.Proof), _jsonSerializerSettings.GetSerializer(paymentMethodId.CryptoCode));
        var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
        res.LinkTemplate = network.BlockExplorerLink;
        return res;
    }

    public void StartBackgroundCheck(Action<Type[]> subscribe)
    {
        subscribe(new[] {typeof(NewOnChainTransactionEvent), typeof(NewBlockEvent)});
    }

    public async Task BackgroundCheck(object o)
    {
        if (o is NewOnChainTransactionEvent newTransaction)
        {
            await UpdatePayoutsAwaitingForPayment(newTransaction);
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

    private async Task UpdatePayoutsInProgress()
    {
        try
        {
            using var ctx = _dbContextFactory.CreateContext();
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

    private async Task UpdatePayoutsAwaitingForPayment(NewOnChainTransactionEvent newTransaction)
    {
        try
        {
            var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(newTransaction.CryptoCode);
            Dictionary<string, decimal> destinations;
            if (newTransaction.NewTransactionEvent.TrackedSource is AddressTrackedSource addressTrackedSource)
            {
                destinations = new Dictionary<string, decimal>()
                {
                    {
                        addressTrackedSource.Address.ToString(),
                        newTransaction.NewTransactionEvent.Outputs.Sum(output => output.Value.GetValue(network))
                    }
                };
            }
            else
            {
                destinations = newTransaction.NewTransactionEvent.TransactionData.Transaction.Outputs
                    .GroupBy(txout => txout.ScriptPubKey)
                    .ToDictionary(
                        txoutSet => txoutSet.Key.GetDestinationAddress(network.NBitcoinNetwork).ToString(),
                        txoutSet => txoutSet.Sum(txout => txout.Value.ToDecimal(MoneyUnit.BTC)));
            }

            var paymentMethodId = new PaymentMethodId(newTransaction.CryptoCode, BitcoinPaymentType.Instance);

            using var ctx = _dbContextFactory.CreateContext();
            var payouts = await ctx.Payouts
                .Include(o => o.PullPaymentData)
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
                if (destination.Value != payoutBlob.CryptoAmount)
                    continue;
                var proof = ParseProof(payout) as PayoutTransactionOnChainBlob;
                if (proof is null)
                {
                    proof = new PayoutTransactionOnChainBlob()
                    {
                        Accounted = !(newTransaction.NewTransactionEvent.TrackedSource is AddressTrackedSource ),
                    };
                }
                var txId = newTransaction.NewTransactionEvent.TransactionData.TransactionHash;
                if (proof.Candidates.Add(txId))
                {
                    if (proof.Accounted is true)
                    {
                        payout.State = PayoutState.InProgress;
                        var walletId = new WalletId(payout.PullPaymentData.StoreId, newTransaction.CryptoCode);
                        _eventAggregator.Publish(new UpdateTransactionLabel(walletId,	
                            newTransaction.NewTransactionEvent.TransactionData.TransactionHash,	
                            UpdateTransactionLabel.PayoutTemplate(payout.Id,payout.PullPaymentDataId, walletId.ToString())));	
                    }
                    if (proof.TransactionId is null)
                        proof.TransactionId = txId;
                    SetProofBlob(payout, proof);
                    
                }
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
