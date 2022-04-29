#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Abstractions.Contracts
{
    public interface ISettingsRepository
    {
        Task<T?> GetSettingAsync<T>(string? name, string? storeId) where T : class;
        Task<T?> GetSettingAsync<T>(string? name = null) where T : class;
        Task UpdateSetting<T>(T obj, string? name, string? storeId ) where T : class;
        Task UpdateSetting<T>(T obj, string? name = null) where T : class;
        Task<T> WaitSettingsChanged<T>(CancellationToken cancellationToken = default) where T : class;

        Task<T> WaitSettingsChanged<T>(string? name = null, string? storeId = null, CancellationToken cancellationToken = default)
            where T : class;
    }
}
