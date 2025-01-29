using NBitcoin;

namespace BTCPayServer.Models.WalletViewModels
{
    public class SigningContextModel
    {
        public SigningContextModel()
        {

        }
        public SigningContextModel(PSBT psbt)
        {
            PSBT = psbt.ToBase64();
        }
        public string PSBT { get; set; }
        public string OriginalPSBT { get; set; }
        public string PayJoinBIP21 { get; set; }
        public bool? EnforceLowR { get; set; }
        public string ChangeAddress { get; set; }

        public string PendingTransactionId { get; set; }
        public long BalanceChangeFromReplacement { get; set; }
    }
}
