using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Authentication
{
    public class PairingCodeEntity
    {
        public string Id
        {
            get;
            set;
        }
        public string Facade
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
        public DateTimeOffset CreatedTime
        {
            get;
            set;
        }
        public DateTimeOffset Expiration
        {
            get;
            set;
        }
        public string TokenValue
        {
            get;
            set;
        }

        public bool IsExpired()
        {
            return DateTimeOffset.UtcNow > Expiration;
        }
    }
}
