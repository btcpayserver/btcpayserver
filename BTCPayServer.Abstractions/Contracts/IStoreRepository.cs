#nullable enable
using System.Threading.Tasks;

namespace BTCPayServer.Abstractions.Contracts;

public interface IStoreRepository
{
    Task<T?> GetSettingAsync<T>(string storeId, string name) where T : class;
    Task UpdateSetting<T>(string storeId, string name, T obj) where T : class;
}
