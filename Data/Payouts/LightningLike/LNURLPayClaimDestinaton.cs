namespace BTCPayServer.Data.Payouts.LightningLike
{
    public class LNURLPayClaimDestinaton : ILightningLikeLikeClaimDestination
    {
        public LNURLPayClaimDestinaton(string lnurl)
        {
            LNURL = lnurl;
        }

        public string Id => null; //lnurls are reusable
        public decimal? Amount { get; } = null;
        public string LNURL { get; set; }

        public override string ToString()
        {
            return LNURL;
        }
    }
}
