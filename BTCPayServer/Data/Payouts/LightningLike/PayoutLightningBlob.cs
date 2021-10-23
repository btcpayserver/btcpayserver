namespace BTCPayServer.Data.Payouts.LightningLike
{
    public class PayoutLightningBlob: IPayoutProof
    {
        public string Bolt11Invoice { get; set; }
        public string Preimage { get; set; }
        public string PaymentHash { get; set; }

        public string ProofType { get; }
        public string Link { get; } = null;
        public string Id => PaymentHash;
    }
}
