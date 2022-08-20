namespace BTCPayServer.Data.Payouts.LightningLike
{
    public class PayoutLightningBlob : IPayoutProof
    {
        public string PaymentHash { get; set; }

        public string ProofType { get; } = "PayoutLightningBlob";
        public string Link { get; } = null;
        public string Id => PaymentHash;
        public string Preimage { get; set; }
    }
}
