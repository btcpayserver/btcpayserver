#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
        PosixSignalRegistration? _signalRegistration;
        readonly object _testingConnectionLock = new object();
        readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public HashSet<string> SupportedCommands { get; private set; } = new HashSet<string>();
        public bool BTCPayHostAvailable { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!processRunner.BTCPayHostEnabled)
            {
                Logs.PayServer.LogInformation("Host integration is disabled");
                return Task.CompletedTask;
            }

            QueueTestConnection();
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                _signalRegistration = PosixSignalRegistration.Create(PosixSignal.SIGHUP, context =>
                {
                    context.Cancel = true;
                    QueueTestConnection();
                });
            }
            return Task.CompletedTask;
        }

        void QueueTestConnection()
        {
            lock (_testingConnectionLock)
                _testingConnection = TestConnectionAfter(_testingConnection);
        }

        async Task TestConnectionAfter(Task? previousTest)
        {
            if (previousTest is not null)
                await previousTest;
            await TestConnection();
        }

        async Task TestConnection()
        {
            var supportedCommands = new HashSet<string>();
            BTCPayHostEnvironment? hostEnvironment = null;
            var hostAvailable = false;
            try
            {
                var env = await processRunner.RunHostCommand(HostCommands.Env, null, _cancellationTokenSource.Token);
                if (env.ExitCode == 0)
                {
                    hostEnvironment = JsonConvert.DeserializeObject<BTCPayHostEnvironment>(env.Output) ??
                                      throw new JsonException("btcpay-host env returned null");
                    foreach (var command in hostEnvironment.Commands ?? [])
                    {
                        if (!string.IsNullOrWhiteSpace(command))
                            supportedCommands.Add(command.Trim());
                    }
                    hostAvailable = true;
                    Logs.PayServer.LogInformation("Host deployment type: {deploymentType}. Supported host commands: {commands}",
                        hostEnvironment.DeploymentType, string.Join(", ", supportedCommands));
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
            BTCPayHostEnvironment = hostEnvironment;
            BTCPayHostAvailable = hostAvailable;
            SupportedCommands = supportedCommands;
        }

        public BTCPayHostEnvironment? BTCPayHostEnvironment { get; set; }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _signalRegistration?.Dispose();
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
