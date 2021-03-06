#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Abstractions.Contracts
{
    public interface ISettingsRepository
    {
        Task<T?> GetSettingAsync<T>(string? name = null) where T : class;
        Task UpdateSetting<T>(T obj, string? name = null) where T : class;
        Task<T> WaitSettingsChanged<T>(CancellationToken cancellationToken = default) where T : class;
    }
}
