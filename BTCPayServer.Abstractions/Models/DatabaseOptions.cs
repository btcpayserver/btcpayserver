using System;
using System.IO;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Extensions.Configuration;

namespace BTCPayServer.Abstractions.Models
{
    public class DatabaseOptions
    {
        public DatabaseOptions(IConfiguration conf, string dataDir)
        {
            var postgresConnectionString = conf["postgres"];
            var mySQLConnectionString = conf["mysql"];
            var sqliteFileName = conf["sqlitefile"];

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
                    : Path.Combine(dataDir, sqliteFileName));

                DatabaseType = DatabaseType.Sqlite;
                ConnectionString = sqliteFileName;
            }
            else
            {
                throw new InvalidOperationException("No database option was configured.");
            }
        }

        public DatabaseType DatabaseType { get; set; }
        public string ConnectionString { get; set; }
    }
}
