using System;
using System.IO;
using BTCPayServer.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Abstractions.Models
{
    public class DatabaseOptions
    {
        public DatabaseType DatabaseType { get; set; }
        public string ConnectionString { get; set; }

        public void Configure(IConfiguration configuration, IOptions<DataDirectories> dataDirectories)
        {
            var postgresConnectionString = configuration["postgres"];
            var mySQLConnectionString = configuration["mysql"];
            var sqliteFileName = configuration["sqlitefile"];

            if (!string.IsNullOrEmpty(postgresConnectionString))
            {
                DatabaseType = DatabaseType.Postgres;
                ConnectionString = postgresConnectionString;
            }
            else if (!string.IsNullOrEmpty(mySQLConnectionString))
            {
                DatabaseType = DatabaseType.MySQL;
                ConnectionString = mySQLConnectionString;
            }
            else if (!string.IsNullOrEmpty(sqliteFileName))
            {
                var connStr = "Data Source=" + (Path.IsPathRooted(sqliteFileName)
                    ? sqliteFileName
                    : Path.Combine(dataDirectories.Value.DataDir, sqliteFileName));

                DatabaseType = DatabaseType.Sqlite;
                ConnectionString = sqliteFileName;
            }
            else
            {
                throw new InvalidOperationException("No database option was configured.");
            }
        }
    }
}
