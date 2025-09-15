using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    /// <inheritdoc />
    public partial class pewfopnq : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "new_subscriber_metadata",
                table: "subscriptions_plan_checkouts",
                type: "jsonb",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "invoice_metadata",
                table: "subscriptions_plan_checkouts",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true,
                oldDefaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "invoice_id",
                table: "subscriptions_plan_checkouts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "is_trial",
                table: "subscriptions_plan_checkouts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "subscriber_id",
                table: "subscriptions_plan_checkouts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "success_redirect_url",
                table: "subscriptions_plan_checkouts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_plan_checkouts_invoice_id",
                table: "subscriptions_plan_checkouts",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_plan_checkouts_subscriber_id",
                table: "subscriptions_plan_checkouts",
                column: "subscriber_id");

            migrationBuilder.AddForeignKey(
                name: "FK_subscriptions_plan_checkouts_Invoices_invoice_id",
                table: "subscriptions_plan_checkouts",
                column: "invoice_id",
                principalTable: "Invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_subscriptions_plan_checkouts_subscriptions_subscribers_subs~",
                table: "subscriptions_plan_checkouts",
                column: "subscriber_id",
                principalTable: "subscriptions_subscribers",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_subscriptions_plan_checkouts_Invoices_invoice_id",
                table: "subscriptions_plan_checkouts");

            migrationBuilder.DropForeignKey(
                name: "FK_subscriptions_plan_checkouts_subscriptions_subscribers_subs~",
                table: "subscriptions_plan_checkouts");

            migrationBuilder.DropIndex(
                name: "IX_subscriptions_plan_checkouts_invoice_id",
                table: "subscriptions_plan_checkouts");

            migrationBuilder.DropIndex(
                name: "IX_subscriptions_plan_checkouts_subscriber_id",
                table: "subscriptions_plan_checkouts");

            migrationBuilder.DropColumn(
                name: "invoice_id",
                table: "subscriptions_plan_checkouts");

            migrationBuilder.DropColumn(
                name: "is_trial",
                table: "subscriptions_plan_checkouts");

            migrationBuilder.DropColumn(
                name: "subscriber_id",
                table: "subscriptions_plan_checkouts");

            migrationBuilder.DropColumn(
                name: "success_redirect_url",
                table: "subscriptions_plan_checkouts");

            migrationBuilder.AlterColumn<string>(
                name: "new_subscriber_metadata",
                table: "subscriptions_plan_checkouts",
                type: "jsonb",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "invoice_metadata",
                table: "subscriptions_plan_checkouts",
                type: "jsonb",
                nullable: true,
                defaultValueSql: "'{}'::jsonb",
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldDefaultValueSql: "'{}'::jsonb");
        }
    }
}
