using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace BTCPayServer.Configuration
{
    public class NBXplorerOptions
    {
        public List<NBXplorerConnectionSetting> NBXplorerConnectionSettings
        {
            get;
            set;
        } = new List<NBXplorerConnectionSetting>();

        public void Configure(IConfiguration conf, BTCPayNetworkProvider provider)
        {
            foreach (BTCPayNetwork btcPayNetwork in provider.GetAll().OfType<BTCPayNetwork>())
            {
                NBXplorerConnectionSetting setting = new NBXplorerConnectionSetting();
                setting.CryptoCode = btcPayNetwork.CryptoCode;
                setting.ExplorerUri = conf.GetOrDefault<Uri>($"{btcPayNetwork.CryptoCode}.explorer.url",
                    btcPayNetwork.NBXplorerNetwork.DefaultSettings.DefaultUrl);
                setting.CookieFile = conf.GetOrDefault<string>($"{btcPayNetwork.CryptoCode}.explorer.cookiefile",
                    btcPayNetwork.NBXplorerNetwork.DefaultSettings.DefaultCookieFile);
                NBXplorerConnectionSettings.Add(setting);
            }
        }
    }
}
