namespace BTCPayServer.Security.Greenfield
{
    public static class GreenfieldConstants
    {
        public const decimal MaxAmount = ulong.MaxValue;
        public const string AuthenticationType = "Greenfield";

        public static class ClaimTypes
        {
            public const string Permission = "APIKey.Permission";
        }
    }
}
