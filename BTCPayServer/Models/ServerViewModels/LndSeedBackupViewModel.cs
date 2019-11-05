using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.ServerViewModels
{
    public class LndSeedBackupViewModel
    {
        public bool IsWalletUnlockPresent { get; set; }

        public string WalletPassword { get; set; }

        public string[] Seed { get; set; }
    }
}
