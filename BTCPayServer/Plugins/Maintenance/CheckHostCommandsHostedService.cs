#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Maintenance
{
    public class CheckHostCommandsHostedService(ProcessRunner processRunner, Logs logs) : IHostedService
    {
        public Logs Logs { get; } = logs;

        Task? _testingConnection;
        readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public HashSet<string> SupportedCommands { get; private set; } = new HashSet<string>();
        public bool BTCPayHostAvailable { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _testingConnection = TestConnection();
            return Task.CompletedTask;
        }

        async Task TestConnection()
        {
            var supportedCommands = new HashSet<string>();
            try
            {
                var env = await processRunner.RunHostCommand(HostCommands.Env, null, _cancellationTokenSource.Token);
                if (env.ExitCode == 0)
                {
                    BTCPayHostEnvironment = JsonConvert.DeserializeObject<BTCPayHostEnvironment>(env.Output) ??
                                            throw new JsonException("btcpay-host env returned null");
                    foreach (var command in BTCPayHostEnvironment.Commands ?? [])
                    {
                        if (!string.IsNullOrWhiteSpace(command))
                            supportedCommands.Add(command.Trim());
                    }
                    BTCPayHostAvailable = true;
                    Logs.PayServer.LogInformation("Host deployment type: {deploymentType}. Supported host commands: {commands}",
                        BTCPayHostEnvironment.DeploymentType, string.Join(", ", supportedCommands));
                }
                else
                {
                    Logs.PayServer.LogInformation($"Call to 'btcpay-host env' failed ({env.Error})");
                }
            }
            catch
            {
                Logs.PayServer.LogInformation("btcpay-host not supported by the host");
            }
            SupportedCommands = supportedCommands;
        }

        public BTCPayHostEnvironment? BTCPayHostEnvironment { get; set; }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _cancellationTokenSource.Cancel();
            try
            {
                if (_testingConnection is not null)
                // Command checks run in the background, so we just wait at most 5 seconds
                    await Task.WhenAny(_testingConnection, Task.Delay(5000, _cancellationTokenSource.Token));
            }
            catch { }
            Logs.PayServer.LogInformation($"{this.GetType().Name} successfully exited...");
        }
    }
}
