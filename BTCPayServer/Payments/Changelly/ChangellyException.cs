using System;

namespace BTCPayServer.Payments.Changelly
{
    public class ChangellyException : Exception
    {
        public ChangellyException(string message) : base(message)
        {
        }
    }
}
