using NBitcoin;

namespace BTCPayServer
{
    public class GetMempoolInfoResponse
    {
        public FeeRate IncrementalRelayFeeRate { get; internal set; }
        public FeeRate MempoolMinfeeRate { get; internal set; }
        public bool? FullRBF { get; internal set; }
    }
}
