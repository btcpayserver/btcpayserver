using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260521120000_multi_sig")]
    public partial class split_pending_transaction_ids : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TABLE "multisig_setups" (
                    id text NOT NULL,
                    store_id text NOT NULL,
                    crypto_code text NOT NULL,
                    data JSONB NOT NULL,
                    expires_at timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_multisig_setups" PRIMARY KEY ("id"),
                    CONSTRAINT "FK_multisig_setups_Stores_store_id" FOREIGN KEY (store_id) REFERENCES "Stores" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "UQ_multisig_setups_store_id_crypto_code" UNIQUE (store_id, crypto_code)
                );

                CREATE INDEX "IX_multisig_setups_store_id" ON "multisig_setups" ("store_id");

                CREATE TABLE multisig_setups_participants (
                    multisig_setup_id text NOT NULL,
                    user_id text NOT NULL,
                    account_key text NULL,
                    account_key_path text NULL,
                    CONSTRAINT "PK_multisig_setups_participants" PRIMARY KEY (multisig_setup_id, user_id),
                    CONSTRAINT "FK_multisig_setups_participants_multisig_setups_multisig_setup_id" FOREIGN KEY (multisig_setup_id) REFERENCES multisig_setups (id) ON DELETE CASCADE,
                    CONSTRAINT "FK_multisig_setups_participants_AspNetUsers_user_id" FOREIGN KEY (user_id) REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "UQ_multisig_setups_participants_multisig_setup_id_account_key" UNIQUE (multisig_setup_id, account_key)
                );

                CREATE INDEX "IX_multisig_setups_participants_user_id" ON "multisig_setups_participants" ("user_id");
                """);

            migrationBuilder.RenameColumn(
                name: "TransactionId",
                table: "PendingTransactions",
                newName: "NoSignatureTransactionId");

            migrationBuilder.RenameIndex(
                name: "IX_PendingTransactions_TransactionId",
                table: "PendingTransactions",
                newName: "IX_PendingTransactions_NoSignatureTransactionId");

            migrationBuilder.AddColumn<string>(
                name: "TransactionId",
                table: "PendingTransactions",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS "multisig_setups_participants";
                DROP TABLE IF EXISTS "multisig_setups";
                """);

            migrationBuilder.DropColumn(
                name: "TransactionId",
                table: "PendingTransactions");

            migrationBuilder.RenameIndex(
                name: "IX_PendingTransactions_NoSignatureTransactionId",
                table: "PendingTransactions",
                newName: "IX_PendingTransactions_TransactionId");

            migrationBuilder.RenameColumn(
                name: "NoSignatureTransactionId",
                table: "PendingTransactions",
                newName: "TransactionId");
        }
    }
}
