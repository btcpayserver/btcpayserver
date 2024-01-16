using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Services;

namespace BTCPayServer.Hosting
{
    public class LoadTranslationsStartupTask(LocalizerService LocalizerService) : IStartupTask
    {
        public Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            // Do not make startup longer for this
            _ = LocalizerService.Load();
            return Task.CompletedTask;
        }
    }
}
