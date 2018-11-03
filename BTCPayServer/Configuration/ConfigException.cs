using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Configuration
{
    public class ConfigException : Exception
    {
        public ConfigException(string message) : base(message)
        {

        }
    }
}
