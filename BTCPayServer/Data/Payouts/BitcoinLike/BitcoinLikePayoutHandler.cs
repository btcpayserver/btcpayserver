using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Payment;
using NBitcoin.RPC;
using NBXplorer.Models;
using NewBlockEvent = BTCPayServer.Events.NewBlockEvent;

public class BitcoinLikePayoutHandler: IPayoutHandler
{
    private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
    private readonly ExplorerClientProvider _explorerClientProvider;
    private readonly BTCPayNetworkJsonSerializerSettings _jsonSerializerSettings;
    private readonly ApplicationDbContextFactory _dbContextFactory;

    public BitcoinLikePayoutHandler(BTCPayNetworkProvider btcPayNetworkProvider, ExplorerClientProvider explorerClientProvider, BTCPayNetworkJsonSerializerSettings jsonSerializerSettings, ApplicationDbContextFactory dbContextFactory )
    {
        _btcPayNetworkProvider = btcPayNetworkProvider;
        _explorerClientProvider = explorerClientProvider;
        _jsonSerializerSettings = jsonSerializerSettings;
        _dbContextFactory = dbContextFactory;
    }
    public bool CanHandle(PaymentMethodId paymentMethod)
    {
        return paymentMethod.PaymentType == BitcoinPaymentType.Instance && _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethod.CryptoCode)?.ReadonlyWallet is false;
    }

    public async Task TrackClaim(PaymentMethodId paymentMethodId, IClaimDestination claimDestination)
    {
        if (CanHandle(paymentMethodId))
        {
            return;
        }
        var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
        var explorerClient = _explorerClientProvider.GetExplorerClient(network);
        if(claimDestination is IBitcoinLikeClaimDestination bitcoinLikeClaimDestination)
            await explorerClient.TrackAsync(TrackedSource.Create(bitcoinLikeClaimDestination.Address));
    }

    public async Task<IClaimDestination> ParseClaimDestination(PaymentMethodId paymentMethodId, string destination)
    {
        if (CanHandle(paymentMethodId))
        {
            return null;
        }

        var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);
        destination = destination.Trim();
        try
        {
            if (destination.StartsWith($"{network.UriScheme}:", StringComparison.OrdinalIgnoreCase))
            {
                return new UriClaimDestination(new BitcoinUrlBuilder(destination, network.NBitcoinNetwork));
            }

            return new AddressClaimDestination(BitcoinAddress.Create(destination, network.NBitcoinNetwork));
        }
        catch
        {
            return null;
        }
    }

    public void StartBackgroundCheck(Action<Type[]> subscribe)
    {
        subscribe(new[] {typeof(NewOnChainTransactionEvent), typeof(NewBlockEvent)});
    }

    public async Task BackgroundCheck(object o)
    {
        
        if (o is NewBlockEvent || o is NewOnChainTransactionEvent)
        {
            await UpdatePayoutsInProgress();
        }
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
                    var proof = payout.GetProofBlob(this._jsonSerializerSettings);
                    var payoutBlob = payout.GetBlob(this._jsonSerializerSettings);
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
                    payout.SetProofBlob(proof, this._jsonSerializerSettings);
                }
                await ctx.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Logs.PayServer.LogWarning(ex, "Error while processing an update in the pull payment hosted service");
            }
        }

}
