using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20230130040047_blob2")]
    public partial class blob2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var type = migrationBuilder.IsNpgsql() ? "JSONB" : "TEXT";
            migrationBuilder.AddColumn<string>(
                name: "Blob2",
                table: "Webhooks",
                type: type,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Blob2",
                table: "WebhookDeliveries",
                type: type,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Blob2",
                table: "PaymentRequests",
                type: type,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Blob2",
                table: "Notifications",
                type: type,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Blob2",
                table: "LightningAddresses",
                type: type,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Blob2",
                table: "Fido2Credentials",
                type: type,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Blob2",
                table: "AspNetUsers",
                type: type,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Blob2",
                table: "ApiKeys",
                type: type,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Blob2",
                table: "Invoices",
                type: type,
                nullable: true);
            migrationBuilder.AddColumn<string>(
              name: "Blob2",
              table: "Payments",
              type: type,
              nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Blob2",
                table: "PayoutProcessors",
                type: type,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Blob2",
                table: "CustodianAccount",
                type: type,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Payments",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Blob2",
                table: "Webhooks");

            migrationBuilder.DropColumn(
                name: "Blob2",
                table: "WebhookDeliveries");

            migrationBuilder.DropColumn(
                name: "Blob2",
                table: "PaymentRequests");

            migrationBuilder.DropColumn(
                name: "Blob2",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Blob2",
                table: "LightningAddresses");

            migrationBuilder.DropColumn(
                name: "Blob2",
                table: "Fido2Credentials");

            migrationBuilder.DropColumn(
                name: "Blob2",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Blob2",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
               name: "Blob2",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Blob2",
                table: "Payments");
            migrationBuilder.DropColumn(
                name: "Blob2",
                table: "PayoutProcessors");

            migrationBuilder.DropColumn(
                name: "Blob2",
                table: "CustodianAccount");

            migrationBuilder.DropColumn(
             name: "Type",
             table: "Payments");
        }
    }
}
