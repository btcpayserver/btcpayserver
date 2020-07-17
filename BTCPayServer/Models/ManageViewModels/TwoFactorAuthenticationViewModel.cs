namespace BTCPayServer.Models.ManageViewModels
{
    public class TwoFactorAuthenticationViewModel
    {

        public int RecoveryCodesLeft { get; set; }

        public bool Is2faEnabled { get; set; }
    }
}
