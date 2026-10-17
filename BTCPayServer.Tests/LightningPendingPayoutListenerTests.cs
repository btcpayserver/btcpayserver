using System;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Data.Payouts.LightningLike;
using BTCPayServer.Lightning;
using BTCPayServer.Payments.Lightning;
using Newtonsoft.Json.Linq;
using Xunit;
using PayoutData = BTCPayServer.Data.PayoutData;

namespace BTCPayServer.Tests;

[Trait("Fast", "Fast")]
public class LightningPendingPayoutListenerTests
{
    [Fact]
    public async Task MissingPaymentWithProofKeepsPayoutInProgress()
    {
        var (payout, proof) = NewPendingPayout();
        var savedProof = payout.Proof;

        await LightningPendingPayoutListener.ApplyPaymentLookup(
            payout, proof, () => Task.FromResult<LightningPayment>(null));

        Assert.Equal(PayoutState.InProgress, payout.State);
        Assert.Equal(savedProof, payout.Proof);
    }

    [Fact]
    public async Task FailedPaymentCancelsPayout()
    {
        var (payout, proof) = NewPendingPayout();

        await LightningPendingPayoutListener.ApplyPaymentLookup(
            payout, proof, () => Task.FromResult(new LightningPayment { Status = LightningPaymentStatus.Failed }));

        Assert.Equal(PayoutState.Cancelled, payout.State);
    }

    [Fact]
    public async Task CompletedPaymentKeepsPreimage()
    {
        var (payout, proof) = NewPendingPayout();
        const string preimage = "synthetic-preimage";

        await LightningPendingPayoutListener.ApplyPaymentLookup(
            payout, proof, () => Task.FromResult(new LightningPayment
            {
                Status = LightningPaymentStatus.Complete,
                Preimage = preimage
            }));

        Assert.Equal(PayoutState.Completed, payout.State);
        Assert.Equal(preimage, JObject.Parse(payout.Proof)["Preimage"]?.Value<string>());
    }

    [Fact]
    public async Task LookupExceptionKeepsPayoutInProgress()
    {
        var (payout, proof) = NewPendingPayout();
        var savedProof = payout.Proof;

        await LightningPendingPayoutListener.ApplyPaymentLookup(
            payout, proof, () => Task.FromException<LightningPayment>(new InvalidOperationException("lookup unavailable")));

        Assert.Equal(PayoutState.InProgress, payout.State);
        Assert.Equal(savedProof, payout.Proof);
    }

    [Fact]
    public async Task CancelledLookupKeepsPayoutInProgress()
    {
        var (payout, proof) = NewPendingPayout();
        var savedProof = payout.Proof;

        await LightningPendingPayoutListener.ApplyPaymentLookup(
            payout, proof, () => Task.FromException<LightningPayment>(new OperationCanceledException()));

        Assert.Equal(PayoutState.InProgress, payout.State);
        Assert.Equal(savedProof, payout.Proof);
    }

    [Theory]
    [InlineData(LightningPaymentStatus.Unknown)]
    [InlineData(LightningPaymentStatus.Pending)]
    public async Task UnresolvedPaymentKeepsPayoutInProgress(LightningPaymentStatus status)
    {
        var (payout, proof) = NewPendingPayout();
        var savedProof = payout.Proof;

        await LightningPendingPayoutListener.ApplyPaymentLookup(
            payout, proof, () => Task.FromResult(new LightningPayment { Status = status }));

        Assert.Equal(PayoutState.InProgress, payout.State);
        Assert.Equal(savedProof, payout.Proof);
    }

    [Fact]
    public async Task MissingProofRetainsExistingCancellationBehavior()
    {
        var payout = new PayoutData { State = PayoutState.InProgress };

        await LightningPendingPayoutListener.ApplyPaymentLookup(
            payout, null, () => throw new InvalidOperationException("lookup should not run"));

        Assert.Equal(PayoutState.Cancelled, payout.State);
    }

    private static (PayoutData Payout, PayoutLightningBlob Proof) NewPendingPayout()
    {
        var payout = new PayoutData { State = PayoutState.InProgress };
        var proof = new PayoutLightningBlob { PaymentHash = new string('a', 64) };
        payout.SetProofBlob(proof, null);
        return (payout, proof);
    }
}
