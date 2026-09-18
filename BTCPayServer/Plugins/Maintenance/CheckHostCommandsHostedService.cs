#nullable enable
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Maintenance
{
    public class CheckHostCommandsHostedService(
        ProcessRunner processRunner,
        HostIntegrationState hostIntegrationState,
        EventAggregator eventAggregator,
        Logs logger)  : EventHostedServiceBase(eventAggregator, logger)
    {
        PosixSignalRegistration? _signalRegistration;

        protected override void SubscribeToEvents()
        {
            if (!processRunner.BTCPayHostEnabled)
            {
                Logs.PayServer.LogInformation("Host integration is disabled");
                return;
            }
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                _signalRegistration = PosixSignalRegistration.Create(PosixSignal.SIGHUP, context =>
                {
                    context.Cancel = true;
                    PushEvent(new object());
                });
            }
            PushEvent(new object());
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            var supportedCommands = new HashSet<string>();
            BTCPayHostEnvironment? hostEnvironment = null;
            var hostAvailable = false;
            try
            {
                var env = await processRunner.RunHostCommand(HostCommands.Env, null, cancellationToken);
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
            hostIntegrationState.Update(new HostIntegrationSnapshot(
                hostAvailable,
                supportedCommands.ToFrozenSet(),
                HostEnvironmentSnapshot.From(hostEnvironment)));
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _signalRegistration?.Dispose();
            return base.StopAsync(cancellationToken);
        }
    }
}
