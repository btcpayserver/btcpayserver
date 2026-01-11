using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace BTCPayServer.Services
{
    public class NBXplorerConnectionFactory : IHostedService
    {
        public NBXplorerConnectionFactory(IOptions<NBXplorerOptions> nbXplorerOptions, Logs logs)
        {
            connectionString = nbXplorerOptions.Value.ConnectionString;
            Logs = logs;
        }
        string connectionString;

        public bool Available { get; set; }
        public Logs Logs { get; }

        async Task IHostedService.StartAsync(CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(connectionString))
            {
                Available = true;
                try
                {
                    await using var conn = await OpenConnection();
                    Logs.Configuration.LogInformation("Connection to NBXplorer's database successful, dashboard and reporting features activated.");
                }
                catch (Exception ex)
                {
                    throw new ConfigException("Error while trying to connection to explorer.postgres: " + ex.Message);
                }
            }
        }

        public async Task<DbConnection> OpenConnection()
        {
            int maxRetries = 10;
            int retries = maxRetries;
retry:
            var conn = new Npgsql.NpgsqlConnection(connectionString);
            try
            {
                await conn.OpenAsync();
            }
            catch (PostgresException ex) when (ex.IsTransient && retries > 0)
            {
                retries--;
                await conn.DisposeAsync();
                await Task.Delay((maxRetries - retries) * 100);
                goto retry;
            }
            return conn;
        }

        Task IHostedService.StopAsync(CancellationToken cancellationToken)
        {
            Npgsql.NpgsqlConnection.ClearAllPools();
            return Task.CompletedTask;
        }
    }
}
