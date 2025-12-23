using System.Collections.Generic;

namespace BTCPayServer.Configuration
{
    public class NBXplorerOptions
    {
        public List<NBXplorerConnectionSetting> NBXplorerConnectionSettings
        {
            get;
            set;
        } = new List<NBXplorerConnectionSetting>();
        public string ConnectionString { get; set; }
    }
}
