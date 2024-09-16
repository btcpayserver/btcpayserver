using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20240827034505_migratepayouts")]
    [DBScript("002.RefactorPayouts.sql")]
    public partial class migratepayouts : DBScriptsMigration
    {
    }
}
