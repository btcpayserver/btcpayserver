using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20240924071444_temprefactor3")]
    [DBScript("006.PaymentsRenaming.sql")]
    public partial class temprefactor3 : DBScriptsMigration
    {
    }
}
