using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20200225133433_AddApiKeyLabel")]
    [DBScript("000.Init.sql")]
    public partial class AddApiKeyLabel : DBScriptsMigration
    {

    }
}
