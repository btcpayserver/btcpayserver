using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20240924065254_monitoredinvoices")]
    [DBScript("000.Init.sql")]
    // Consolidated through 2.0.0. Keep the original ID so existing databases skip initialization.
    public partial class monitoredinvoices : DBScriptsMigration
    {
    }
}
