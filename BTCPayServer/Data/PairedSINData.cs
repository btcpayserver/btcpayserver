using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Data
{
    public class PairedSINData
    {
        public string Id
        {
            get; set;
        }

        public string Facade
        {
            get; set;
        }

        public string StoreDataId
        {
            get; set;
        }

        public StoreData StoreData { get; set; }

        public string Label
        {
            get;
            set;
        }
        public DateTimeOffset PairingTime
        {
            get;
            set;
        }
        public string SIN
        {
            get; set;
        }
    }
}
