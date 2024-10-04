using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20240924065254_monitoredinvoices")]
    [DBScript("004.MonitoredInvoices.sql")]
    public partial class monitoredinvoices : DBScriptsMigration
    {
    }
}
