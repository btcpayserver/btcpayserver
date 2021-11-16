using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using NBitcoin;

namespace BTCPayServer.Models.StoreViewModels
{
    public class DerivationSchemeViewModel
    {

        [Display(Name = "Derivation scheme")]
        public string DerivationScheme { get; set; }

        public List<(string KeyPath, string Address, RootedKeyPath RootedKeyPath)> AddressSamples
        {
            get; set;
        } = new List<(string KeyPath, string Address, RootedKeyPath RootedKeyPath)>();

        public string CryptoCode { get; set; }
        public string KeyPath { get; set; }
        [Display(Name = "Root fingerprint")]
        public string RootFingerprint { get; set; }
        public bool Confirmation { get; set; }

        [Display(Name = "Wallet file")]
        public IFormFile WalletFile { get; set; }
        [Display(Name = "Wallet file content")]
        public string WalletFileContent { get; set; }
        public string Config { get; set; }
        public string Source { get; set; }
        [Display(Name = "Derivation scheme format")]
        public string DerivationSchemeFormat { get; set; }
        [Display(Name = "Account key")]
        public string AccountKey { get; set; }
        public BTCPayNetwork Network { get; set; }
        [Display(Name = "Can use hot wallet")]
        public bool CanUseHotWallet { get; set; }
        [Display(Name = "Can use RPC import")]
        public bool CanUseRPCImport { get; set; }
        public bool SupportSegwit { get; set; }
        public bool SupportTaproot { get; set; }
        public RootedKeyPath GetAccountKeypath()
        {
            if (KeyPath != null && RootFingerprint != null &&
                NBitcoin.KeyPath.TryParse(KeyPath, out var p) &&
                HDFingerprint.TryParse(RootFingerprint, out var fp))
            {
                return new RootedKeyPath(fp, p);
            }
            return null;
        }
    }
}
