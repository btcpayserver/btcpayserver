namespace BTCPayServer.Security
{
    public class AuthenticationSchemes
    {
        public const string Cookie = "Identity.Application";
        public const string Bitpay = "Bitpay";
        public const string ApiKey = "GreenfieldApiKey";
        public const string Basic = "Basic";
        public const string ApiKeyOrBasic = ApiKey + "," + Basic;
    }
}
