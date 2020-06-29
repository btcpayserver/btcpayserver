using System.Collections.Generic;

namespace BTCPayServer.Models.StoreViewModels
{
    public class StoresViewModel
    {
        public List<StoreViewModel> Stores
        {
            get; set;
        } = new List<StoreViewModel>();

        public class StoreViewModel
        {
            public string Name
            {
                get; set;
            }

            public string WebSite
            {
                get; set;
            }

            public string Id
            {
                get; set;
            }
            public bool IsOwner
            {
                get;
                set;
            }
        }
    }
}
