using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260126093615_wh_delivery_time")]
    public partial class wh_delivery_time : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                 ALTER TABLE "WebhookDeliveries"
                                 ADD COLUMN "DeliveryTime" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT now();
                                 -- noinspection SqlWithoutWhere
                                 UPDATE "WebhookDeliveries" SET "DeliveryTime" = "Timestamp";
                                 """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
