﻿#if ALTCOINS
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Services.Altcoins.Ethereum.Configuration
{
    public class EthereumLikeConfiguration
    {
        public static string SettingsKey(int chainId)
        {
            return $"{nameof(EthereumLikeConfiguration)}_{chainId}";
        }
        public int ChainId { get; set; }
        [Display(Name = "Web3 provider url")]
        public string Web3ProviderUrl { get; set; }
        
        [Display(Name = "Web3 provider username (can be left blank)")]
        public string Web3ProviderUsername { get; set; }
        
        [Display(Name = "Web3 provider password (can be left blank)")]
        public string Web3ProviderPassword { get; set; }

        public override string ToString()
        {
            return "";
        }
    }
}
#endif
                                                                                                                                                                  
