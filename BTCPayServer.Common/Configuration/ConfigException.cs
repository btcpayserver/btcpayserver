using System;

namespace BTCPayServer.Configuration
{
    public class ConfigException : Exception
    {
        public ConfigException(string message) : base(message)
        {

        }
    }
}
