namespace BTCPayServer.Abstractions.Constants
{
    public class AuthenticationSchemes
    {
        public const string Cookie = "Identity.Application";
        /// <summary>
        /// The user could use his password; however, some policies prevented him access to BTCPay Server.
        /// </summary>
        public const string LimitedLogin = "LimitedLogin";

        public const string Bitpay = "Bitpay";
        public const string Greenfield = "Greenfield.APIKeys,Greenfield.Basic";
        public const string GreenfieldAPIKeys = "Greenfield.APIKeys";
        public const string GreenfieldBasic = "Greenfield.Basic";
    }
}
