using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BTCPayServer.Migrations
{
    public static class MigrationsExtensions
    {
        public static bool SupportDropColumn(this Microsoft.EntityFrameworkCore.Migrations.Migration migration, string activeProvider)
        {
            return activeProvider != "Microsoft.EntityFrameworkCore.Sqlite";
        }

        public static bool SupportDropForeignKey(this Microsoft.EntityFrameworkCore.Migrations.Migration migration, string activeProvider)
        {
            return activeProvider != "Microsoft.EntityFrameworkCore.Sqlite";
        }
        public static bool SupportDropForeignKey(this DatabaseFacade facade)
        {
            return facade.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite";
        }
        public static bool IsMySql(this Microsoft.EntityFrameworkCore.Migrations.Migration migration, string activeProvider)
        {
            return activeProvider == "Pomelo.EntityFrameworkCore.MySql";
        }
    }
}
