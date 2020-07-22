using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using NBitcoin;

namespace BTCPayServer.Models.StoreViewModels
{
    public class RecoverySeedBackupViewModel
    {
        public string CryptoCode { get; set; }
        public string Mnemonic { get; set; }
        public string Passphrase { get; set; }
        public bool IsStored { get; set; }
        public string ReturnUrl { get; set; }

        public string[] Words
        {
            get => Mnemonic.Split(" ");
        }
    }
}
