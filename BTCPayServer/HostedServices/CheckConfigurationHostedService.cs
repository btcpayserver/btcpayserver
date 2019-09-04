using System;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services;
using Microsoft.Extensions.Hosting;
using System.Threading;
using BTCPayServer.Configuration;
using BTCPayServer.Logging;
using NBitcoin.DataEncoders;

namespace BTCPayServer.HostedServices
{
    public class CheckConfigurationHostedService : IHostedService
    {
        private readonly BTCPayServerOptions _options;

        public CheckConfigurationHostedService(BTCPayServerOptions options)
        {
            _options = options;
        }

        public bool CanUseSSH { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = TestConnection();
            return Task.CompletedTask;
        }

        async Task TestConnection()
        {
            var canUseSSH = false;
            if (_options.SSHSettings != null)
            {
                Logs.Configuration.LogInformation($"SSH settings detected, testing connection to {_options.SSHSettings.Username}@{_options.SSHSettings.Server} on port {_options.SSHSettings.Port} ...");
                try
                {
                    using (var connection = await _options.SSHSettings.ConnectAsync())
                    {
                        await connection.DisconnectAsync();
                        Logs.Configuration.LogInformation($"SSH connection succeeded");
                        canUseSSH = true;
                    }
                }
                catch (Renci.SshNet.Common.SshAuthenticationException ex)
                {
                    Logs.Configuration.LogWarning($"SSH invalid credentials ({ex.Message})");
                }
                catch (Exception ex)
                {
                    var message = ex.Message;
                    if (ex is AggregateException aggrEx && aggrEx.InnerException?.Message != null)
                    {
                        message = aggrEx.InnerException.Message;
                    }
                    Logs.Configuration.LogWarning($"SSH connection issue of type {ex.GetType().Name}: {message}");
                }
            }
            CanUseSSH = canUseSSH;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
