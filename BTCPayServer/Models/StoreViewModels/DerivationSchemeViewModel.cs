using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using NBitcoin;

namespace BTCPayServer.Models.StoreViewModels
{
    public class DerivationSchemeViewModel
    {

        public DerivationSchemeViewModel()
        {
        }

        [Display(Name = "Derivation scheme")]
        public string DerivationScheme
        {
            get; set;
        }

        public List<(string KeyPath, string Address, RootedKeyPath RootedKeyPath)> AddressSamples
        {
            get; set;
        } = new List<(string KeyPath, string Address, RootedKeyPath RootedKeyPath)>();

        public string CryptoCode { get; set; }
        public string KeyPath { get; set; }
        public string RootFingerprint { get; set; }
        [Display(Name = "Hint address")]
        public string HintAddress { get; set; }
        public bool Confirmation { get; set; }
        public bool Enabled { get; set; } = true;

        public KeyPath RootKeyPath { get; set; }

        [Display(Name = "Wallet File")]
        public IFormFile WalletFile { get; set; }
        public string Config { get; set; }
        public string Source { get; set; }
        public string DerivationSchemeFormat { get; set; }
        public string AccountKey { get; set; }
        public BTCPayNetwork Network { get; set; }
        public bool CanUseHotWallet { get; set; }
        public bool CanUseRPCImport { get; set; }

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
