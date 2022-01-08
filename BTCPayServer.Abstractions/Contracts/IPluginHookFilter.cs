using System.Threading.Tasks;

namespace BTCPayServer.Abstractions.Contracts
{
    public interface IPluginHookFilter
    {
        public string Hook { get; }

        Task<object> Execute(object args);
    }
}
