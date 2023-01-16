namespace BTCPayServer.Data.Payouts.LightningLike
{
    public class PayoutLightningBlob : IPayoutProof
    {
        public string PaymentHash { get; set; }

        public static string PayoutLightningBlobProofType = "PayoutLightningBlob";
        public string ProofType { get; } = PayoutLightningBlobProofType;
        public string Link { get; } = null;
        public string Id => PaymentHash;
        public string Preimage { get; set; }
    }
}
