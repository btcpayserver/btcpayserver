#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Abstractions.Contracts;

public interface IStoreSettingsRepository
{
    Task<T?> GetSettingAsync<T>(string storeId, string name, bool cache) where T : class;
    Task UpdateSetting<T>(string storeId, string name, T obj) where T : class;

    Task<T> WaitSettingsChanged<T>(string storeId, string name, CancellationToken cancellationToken = default)
        where T : class;
}
