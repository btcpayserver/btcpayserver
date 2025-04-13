using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20240913034505_refactorpendinginvoicespayments")]
    [DBScript("003.RefactorPendingInvoicesPayments.sql")]
    public partial class refactorpendinginvoicespayments : DBScriptsMigration
    {
    }
}
