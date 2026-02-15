using System.Threading.Tasks;

namespace BTCPayServer.Services.GlobalSearch
{
    /// <summary>
    /// Allows plugins to append or modify global search results.
    /// Register implementations in DI and they will be executed during /search/global.
    /// </summary>
    public interface IGlobalSearchProvider
    {
        Task Search(GlobalSearchPluginContext context);
    }
}
