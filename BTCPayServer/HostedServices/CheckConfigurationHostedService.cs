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

        public Task StartAsync(CancellationToken cancellationToken)
        {
            new Thread(() =>
            {
                if (_options.SSHSettings != null)
                {
                    Logs.Configuration.LogInformation($"SSH settings detected, testing connection to {_options.SSHSettings.Username}@{_options.SSHSettings.Server} on port {_options.SSHSettings.Port} ...");
                    var connection = new Renci.SshNet.SshClient(_options.SSHSettings.CreateConnectionInfo());
                    connection.HostKeyReceived += (object sender, Renci.SshNet.Common.HostKeyEventArgs e) =>
                    {
                        e.CanTrust = true;
                        if (!_options.IsTrustedFingerprint(e.FingerPrint, e.HostKey))
                        {
                            Logs.Configuration.LogWarning($"SSH host fingerprint for {e.HostKeyName} is untrusted, start BTCPay with -sshtrustedfingerprints \"{Encoders.Hex.EncodeData(e.FingerPrint)}\"");
                        }
                    };
                    try
                    {
                        connection.Connect();
                        connection.Disconnect();
                        Logs.Configuration.LogInformation($"SSH connection succeeded");
                    }
                    catch (Renci.SshNet.Common.SshAuthenticationException)
                    {
                        Logs.Configuration.LogWarning($"SSH invalid credentials");
                    }
                    catch (Exception ex)
                    {
                        var message = ex.Message;
                        if (ex is AggregateException aggrEx && aggrEx.InnerException?.Message != null)
                        {
                            message = aggrEx.InnerException.Message;
                        }
                        Logs.Configuration.LogWarning($"SSH connection issue: {message}");
                    }
                    finally
                    {
                        connection.Dispose();
                    }
                }
            })
            { IsBackground = true }.Start();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
