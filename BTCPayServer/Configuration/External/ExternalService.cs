using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Lightning;

namespace BTCPayServer.Configuration.External
{
    public class ExternalServices : MultiValueDictionary<string, ExternalService>
    {
        public IEnumerable<T> GetServices<T>(string cryptoCode) where T : ExternalService
        {
            if (!this.TryGetValue(cryptoCode.ToUpperInvariant(), out var services))
                return Array.Empty<T>();
            return services.OfType<T>();
        }
    }

    public class ExternalService
    {
    }
}
