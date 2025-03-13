using System;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace BTCPayServer.Models.StoreViewModels
{
    public enum WalletSetupMethod
    {
        ImportOptions,
        Hardware,
        File,
        Xpub,
        Scan,
        Seed,
        GenerateOptions,
        HotWallet,
        WatchOnly
    }

    public class WalletSetupViewModel : DerivationSchemeViewModel
    {
        public WalletSetupMethod? Method { get; set; }
        public WalletSetupRequest SetupRequest { get; set; }
        public string StoreId { get; set; }
        public bool IsHotWallet { get; set; }

        public string ViewName =>
            Method switch
            {
                WalletSetupMethod.ImportOptions => "ImportWalletOptions",
                WalletSetupMethod.Hardware => "ImportWallet/Hardware",
                WalletSetupMethod.Xpub => "ImportWallet/Xpub",
                WalletSetupMethod.File => "ImportWallet/File",
                WalletSetupMethod.Scan => "ImportWallet/Scan",
                WalletSetupMethod.Seed => "ImportWallet/Seed",
                WalletSetupMethod.GenerateOptions => "GenerateWalletOptions",
                WalletSetupMethod.HotWallet => "GenerateWallet",
                WalletSetupMethod.WatchOnly => "GenerateWallet",
                _ => "SetupWallet"
            };

        internal void SetPermission(WalletCreationPermissions perm)
        {
            this.CanCreateNewColdWallet = perm.CanCreateColdWallet;
            this.CanUseHotWallet = perm.CanCreateHotWallet;
            this.CanUseRPCImport = perm.CanRPCImport;
        }
        public void SetViewData(ViewDataDictionary ViewData)
        {
            ViewData.Add(nameof(CanUseHotWallet), CanUseHotWallet);
            ViewData.Add(nameof(CanCreateNewColdWallet), CanCreateNewColdWallet);
            ViewData.Add(nameof(CanUseRPCImport), CanUseRPCImport);
            ViewData.Add(nameof(SupportSegwit), SupportSegwit);
            ViewData.Add(nameof(SupportTaproot), SupportTaproot);
            ViewData.Add(nameof(Method), Method);
        }
    }
}
