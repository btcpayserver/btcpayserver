using System.Collections.Generic;

namespace BTCPayServer.Extensions.BlockExplorerLinks
{
    public class ExplorerLinksViewModel
    {
        public List<Item> Items { get; set; }

        public class Item
        {
            public string CryptoCode { get; set; }
            public string Name { get; set; }
            public string Link { get; set; }
            public string DefaultLink { get; set; }
        }
    }
}
