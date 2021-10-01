namespace BTCPayServer.Data.Payouts.LightningLike
{
    public class LNURLPayClaimDestinaton: ILightningLikeLikeClaimDestination
    {
        private readonly string _lnurl;

        public LNURLPayClaimDestinaton(string lnurl)
        {
            LNURL = lnurl;
        }

        public decimal? Amount { get; } = null;
        public string LNURL { get; set; }

        public override string ToString()
        {
            return LNURL;
        }
    }
}
