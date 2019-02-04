using System;

namespace BTCPayServer.Payments.AutoTrade
{
    public class AutoTradeException : Exception
    {
        public AutoTradeException(string message) : base(message)
        {
        }
    }
}
