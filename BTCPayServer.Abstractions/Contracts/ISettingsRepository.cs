using System.Threading;
using System.Threading.Tasks;

namespace BTCPayServer.Abstractions.Contracts
{
    public interface ISettingsRepository
    {
        Task<T> GetSettingAsync<T>(string name = null);
        Task UpdateSetting<T>(T obj, string name = null);
        Task<T> WaitSettingsChanged<T>(CancellationToken cancellationToken = default);
    }
}
