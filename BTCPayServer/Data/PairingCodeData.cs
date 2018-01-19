using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Data
{
    public class PairingCodeData
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
        public DateTimeOffset Expiration
        {
            get;
            set;
        }

        public string Label
        {
            get;
            set;
        }
        public string SIN
        {
            get;
            set;
        }
        public DateTime DateCreated
        {
            get;
            set;
        }
        public string TokenValue
        {
            get;
            set;
        }
    }
}
