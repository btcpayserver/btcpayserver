using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20231020135844_AddBoltcardsTable")]
    [DBScript("000.Init.sql")]
    public partial class AddBoltcardsTable : DBScriptsMigration
    {
    }
}
