using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Maintenance
{
    public class CheckHostCommandsHostedService : IHostedService
    {
        public Logs Logs { get; }

        private readonly ProcessRunner _processRunner;
        Task _testingConnection;
        readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public CheckHostCommandsHostedService(ProcessRunner processRunner, Logs logs)
        {
            Logs = logs;
            _processRunner = processRunner;
        }

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
                var commands = await _processRunner.RunHostCommand(HostCommands.Commands, null, _cancellationTokenSource.Token);
                if (commands.ExitCode == 0)
                {
                    var parsedCommands = JsonSerializer.Deserialize<string[]>(commands.Output) ?? [];
                    foreach (var command in parsedCommands)
                    {
                        if (!string.IsNullOrWhiteSpace(command))
                            supportedCommands.Add(command.Trim());
                    }
                    BTCPayHostAvailable = true;
                    Logs.PayServer.LogInformation("Supported host commands: {commands}", string.Join(", ", supportedCommands));
                }
                else
                {
                    Logs.PayServer.LogInformation($"Call to 'btcpay-host commands' failed ({commands.Error})");
                }
            }
            catch
            {
                Logs.PayServer.LogInformation("btcpay-host not supported by the host");
            }
            SupportedCommands = supportedCommands;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _cancellationTokenSource.Cancel();
            try
            {
                // Command checks run in the background, so we just wait at most 5 seconds
                await Task.WhenAny(_testingConnection, Task.Delay(5000, _cancellationTokenSource.Token));
            }
            catch { }
            Logs.PayServer.LogInformation($"{this.GetType().Name} successfully exited...");
        }
    }
}
