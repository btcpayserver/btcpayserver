﻿using System;
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
        Task _testingConnection;
        CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        public CheckConfigurationHostedService(BTCPayServerOptions options)
        {
            _options = options;
        }

        public bool CanUseSSH { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Logs.PayServer.LogInformation($"Starting {this.GetType().Name}");
            _testingConnection = TestConnection();
            Logs.PayServer.LogInformation($"Started {this.GetType().Name}");
            return Task.CompletedTask;
        }

        async Task TestConnection()
        {
            TimeSpan nextWait = TimeSpan.FromSeconds(10);
            retry:
            var canUseSSH = false;
            if (_options.SSHSettings != null)
            {
                Logs.Configuration.LogInformation($"SSH settings detected, testing connection to {_options.SSHSettings.Username}@{_options.SSHSettings.Server} on port {_options.SSHSettings.Port} ...");
                try
                {
                    using (var connection = await _options.SSHSettings.ConnectAsync(_cancellationTokenSource.Token))
                    {
                        await connection.DisconnectAsync(_cancellationTokenSource.Token);
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
                if (!canUseSSH)
                {
                    Logs.Configuration.LogWarning($"Retrying SSH connection in {(int)nextWait.TotalSeconds} seconds");
                    await Task.Delay(nextWait, _cancellationTokenSource.Token);
                    nextWait = TimeSpan.FromSeconds(nextWait.TotalSeconds * 2);
                    if (nextWait > TimeSpan.FromMinutes(10.0))
                        nextWait = TimeSpan.FromMinutes(10.0);
                    goto retry;
                }
            }
            CanUseSSH = canUseSSH;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _cancellationTokenSource.Cancel();
            try
            {
                // Renci SSH sometimes is deadlocking, so we just wait at most 5 seconds
                await Task.WhenAny(_testingConnection, Task.Delay(5000, _cancellationTokenSource.Token));
            }
            catch { }
            Logs.PayServer.LogInformation($"{this.GetType().Name} successfully exited...");
        }
    }
}
