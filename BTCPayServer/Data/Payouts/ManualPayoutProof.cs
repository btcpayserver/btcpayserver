namespace BTCPayServer.Data
{
    public class ManualPayoutProof : IPayoutProof
    {
        public static string Type = "ManualPayoutProof";
        public string ProofType { get; } = Type;
        public string Link { get; set; }
        public string Id { get; set; }
    }
}
