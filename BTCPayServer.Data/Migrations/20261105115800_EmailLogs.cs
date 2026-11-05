using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261105115800_EmailLogs")]
    public partial class EmailLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE email_logs (
                    id text NOT NULL,
                    store_id text NULL,
                    created_at timestamp with time zone NOT NULL DEFAULT NOW(),
                    status text NOT NULL,
                    trigger text NOT NULL,
                    "to" text[] NOT NULL DEFAULT '{}',
                    cc text[] NOT NULL DEFAULT '{}',
                    bcc text[] NOT NULL DEFAULT '{}',
                    subject text NOT NULL,
                    body text NOT NULL,
                    error text NULL,
                    CONSTRAINT "PK_email_logs" PRIMARY KEY (id),
                    CONSTRAINT "FK_email_logs_Stores_store_id" FOREIGN KEY (store_id) REFERENCES "Stores" ("Id") ON DELETE CASCADE
                );

                CREATE INDEX "IX_email_logs_store_id_created_at" ON email_logs (store_id, created_at DESC);
                CREATE INDEX "IX_email_logs_created_at" ON email_logs (created_at);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TABLE IF EXISTS email_logs;""");
        }
    }
}
