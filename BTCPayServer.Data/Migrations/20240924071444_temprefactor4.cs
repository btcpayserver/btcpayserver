using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20240924071444_temprefactor4")]
    [DBScript("007.PaymentsRenaming.sql")]
    public partial class temprefactor4 : DBScriptsMigration
    {
    }
}
