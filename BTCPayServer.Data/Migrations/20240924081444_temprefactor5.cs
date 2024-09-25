using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20240924081444_temprefactor5")]
    [DBScript("008.PaymentsRenaming.sql")]
    public partial class temprefactor5 : DBScriptsMigration
    {
    }
}
