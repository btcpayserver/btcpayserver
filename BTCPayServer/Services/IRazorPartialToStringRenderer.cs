using System.Threading.Tasks;

namespace BTCPayServer.Services;

public interface IRazorPartialToStringRenderer
{
    Task<string> RenderPartialToStringAsync<TModel>(string partialName, TModel model);
}
