using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using NBitcoin;

namespace BTCPayServer.Models.StoreViewModels
{
    public class DerivationSchemeViewModel
    {
        public DerivationSchemeViewModel()
        {
        }
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

        [Display(Name = "Coldcard Wallet File")]
        public IFormFile ColdcardPublicFile{ get; set; }
        public string Config { get; set; }
        public string Source { get; set; }
        public string DerivationSchemeFormat { get; set; }
        public string AccountKey { get; set; }
        public BTCPayNetwork Network { get; set; }
        public bool CanUseHotWallet { get; set; }

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
