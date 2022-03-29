using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Abstractions.Contracts;

public interface ISwaggerProvider
{
    Task<JObject> Fetch();
}
