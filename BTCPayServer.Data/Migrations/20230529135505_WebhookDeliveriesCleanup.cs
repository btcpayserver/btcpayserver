using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NBitcoin;
using Newtonsoft.Json;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20230529135505_WebhookDeliveriesCleanup")]
    public partial class WebhookDeliveriesCleanup : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.IsNpgsql())
            {
                migrationBuilder.Sql("DROP TABLE IF EXISTS \"InvoiceWebhookDeliveries\", \"WebhookDeliveries\";");

                migrationBuilder.CreateTable(
                name: "WebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    WebhookId = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Pruned = table.Column<bool>(type: "BOOLEAN", nullable: false),
                    Blob = table.Column<string>(type: "JSONB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookDeliveries_Webhooks_WebhookId",
                        column: x => x.WebhookId,
                        principalTable: "Webhooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });


                migrationBuilder.CreateIndex(
                    name: "IX_WebhookDeliveries_WebhookId",
                    table: "WebhookDeliveries",
                    column: "WebhookId");
                migrationBuilder.Sql("CREATE INDEX \"IX_WebhookDeliveries_Timestamp\" ON \"WebhookDeliveries\"(\"Timestamp\") WHERE \"Pruned\" IS FALSE");

                migrationBuilder.CreateTable(
                name: "InvoiceWebhookDeliveries",
                columns: table => new
                {
                    InvoiceId = table.Column<string>(type: "TEXT", nullable: false),
                    DeliveryId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceWebhookDeliveries", x => new { x.InvoiceId, x.DeliveryId });
                    table.ForeignKey(
                        name: "FK_InvoiceWebhookDeliveries_WebhookDeliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "WebhookDeliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvoiceWebhookDeliveries_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
