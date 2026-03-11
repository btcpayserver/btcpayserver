using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20251028061727_subs")]
    public partial class subs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "offering_id",
                table: "email_rules",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    store_id = table.Column<string>(type: "text", nullable: false),
                    external_ref = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "TEXT", nullable: false, defaultValueSql: "''::TEXT"),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    additional_data = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.id);
                    table.ForeignKey(
                        name: "FK_customers_Stores_store_id",
                        column: x => x.store_id,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subs_offerings",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    app_id = table.Column<string>(type: "text", nullable: false),
                    success_redirect_url = table.Column<string>(type: "text", nullable: true),
                    payment_reminder_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    additional_data = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subs_offerings", x => x.id);
                    table.ForeignKey(
                        name: "FK_subs_offerings_Apps_app_id",
                        column: x => x.app_id,
                        principalTable: "Apps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customers_identities",
                columns: table => new
                {
                    customer_id = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers_identities", x => new { x.customer_id, x.type });
                    table.ForeignKey(
                        name: "FK_customers_identities_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subs_features",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    custom_id = table.Column<string>(type: "text", nullable: false),
                    offering_id = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subs_features", x => x.id);
                    table.ForeignKey(
                        name: "FK_subs_features_subs_offerings_offering_id",
                        column: x => x.offering_id,
                        principalTable: "subs_offerings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subs_plans",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    offering_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    price = table.Column<decimal>(type: "numeric", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    recurring_type = table.Column<string>(type: "text", nullable: false),
                    grace_period_days = table.Column<int>(type: "integer", nullable: false),
                    trial_days = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    members_count = table.Column<int>(type: "integer", nullable: false),
                    monthly_revenue = table.Column<decimal>(type: "numeric", nullable: false),
                    optimistic_activation = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    renewable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    additional_data = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subs_plans", x => x.id);
                    table.ForeignKey(
                        name: "FK_subs_plans_subs_offerings_offering_id",
                        column: x => x.offering_id,
                        principalTable: "subs_offerings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subs_plan_changes",
                columns: table => new
                {
                    plan_id = table.Column<string>(type: "text", nullable: false),
                    plan_change_id = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subs_plan_changes", x => new { x.plan_id, x.plan_change_id });
                    table.ForeignKey(
                        name: "FK_subs_plan_changes_subs_plans_plan_change_id",
                        column: x => x.plan_change_id,
                        principalTable: "subs_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_subs_plan_changes_subs_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "subs_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subs_plans_features",
                columns: table => new
                {
                    plan_id = table.Column<string>(type: "text", nullable: false),
                    feature_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subs_plans_features", x => new { x.plan_id, x.feature_id });
                    table.ForeignKey(
                        name: "FK_subs_plans_features_subs_features_feature_id",
                        column: x => x.feature_id,
                        principalTable: "subs_features",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_subs_plans_features_subs_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "subs_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subs_subscribers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    offering_id = table.Column<string>(type: "text", nullable: false),
                    customer_id = table.Column<string>(type: "text", nullable: false),
                    plan_id = table.Column<string>(type: "text", nullable: false),
                    new_plan_id = table.Column<string>(type: "text", nullable: true),
                    paid_amount = table.Column<decimal>(type: "numeric", nullable: true),
                    processing_invoice_id = table.Column<string>(type: "text", nullable: true),
                    phase = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'Expired'::TEXT"),
                    plan_started = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    optimistic_activation = table.Column<bool>(type: "boolean", nullable: false),
                    trial_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    grace_period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    auto_renew = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    payment_reminder_days = table.Column<int>(type: "integer", nullable: true),
                    payment_reminded = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    suspended = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    test_account = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    suspension_reason = table.Column<string>(type: "text", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    additional_data = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subs_subscribers", x => x.id);
                    table.ForeignKey(
                        name: "FK_subs_subscribers_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_subs_subscribers_subs_offerings_offering_id",
                        column: x => x.offering_id,
                        principalTable: "subs_offerings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_subs_subscribers_subs_plans_new_plan_id",
                        column: x => x.new_plan_id,
                        principalTable: "subs_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_subs_subscribers_subs_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "subs_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subs_plan_checkouts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    invoice_id = table.Column<string>(type: "text", nullable: true),
                    success_redirect_url = table.Column<string>(type: "text", nullable: true),
                    is_trial = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    plan_id = table.Column<string>(type: "text", nullable: false),
                    new_subscriber = table.Column<bool>(type: "boolean", nullable: false),
                    new_subscriber_email = table.Column<string>(type: "text", nullable: true),
                    subscriber_id = table.Column<long>(type: "bigint", nullable: true),
                    invoice_metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    new_subscriber_metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    test_account = table.Column<bool>(type: "boolean", nullable: false),
                    credited = table.Column<decimal>(type: "numeric", nullable: false),
                    plan_started = table.Column<bool>(type: "boolean", nullable: false),
                    credit_purchase = table.Column<decimal>(type: "numeric", nullable: true),
                    refund_amount = table.Column<decimal>(type: "numeric", nullable: true),
                    on_pay = table.Column<string>(type: "text", nullable: false, defaultValue: "SoftMigration"),
                    base_url = table.Column<string>(type: "text", nullable: false),
                    expiration = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() + interval '1 day'"),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    additional_data = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subs_plan_checkouts", x => x.id);
                    table.ForeignKey(
                        name: "FK_subs_plan_checkouts_Invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_subs_plan_checkouts_subs_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "subs_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_subs_plan_checkouts_subs_subscribers_subscriber_id",
                        column: x => x.subscriber_id,
                        principalTable: "subs_subscribers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "subs_portal_sessions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    subscriber_id = table.Column<long>(type: "bigint", nullable: false),
                    expiration = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now() + interval '1 day'"),
                    base_url = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subs_portal_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_subs_portal_sessions_subs_subscribers_subscriber_id",
                        column: x => x.subscriber_id,
                        principalTable: "subs_subscribers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subs_subscriber_credits",
                columns: table => new
                {
                    subscriber_id = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subs_subscriber_credits", x => new { x.subscriber_id, x.currency });
                    table.ForeignKey(
                        name: "FK_subs_subscriber_credits_subs_subscribers_subscriber_id",
                        column: x => x.subscriber_id,
                        principalTable: "subs_subscribers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscriber_invoices",
                columns: table => new
                {
                    invoice_id = table.Column<string>(type: "text", nullable: false),
                    subscriber_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriber_invoices", x => new { x.subscriber_id, x.invoice_id });
                    table.ForeignKey(
                        name: "FK_subscriber_invoices_Invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_subscriber_invoices_subs_subscribers_subscriber_id",
                        column: x => x.subscriber_id,
                        principalTable: "subs_subscribers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subs_subscriber_credits_history",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    subscriber_id = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()"),
                    description = table.Column<string>(type: "text", nullable: false),
                    debit = table.Column<decimal>(type: "numeric", nullable: false),
                    credit = table.Column<decimal>(type: "numeric", nullable: false),
                    balance = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subs_subscriber_credits_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subs_subscriber_credits_history_subs_subscriber_credits_sub~",
                        columns: x => new { x.subscriber_id, x.currency },
                        principalTable: "subs_subscriber_credits",
                        principalColumns: new[] { "subscriber_id", "currency" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_email_rules_offering_id",
                table: "email_rules",
                column: "offering_id");

            migrationBuilder.CreateIndex(
                name: "IX_customers_store_id_external_ref",
                table: "customers",
                columns: new[] { "store_id", "external_ref" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subs_features_offering_id_custom_id",
                table: "subs_features",
                columns: new[] { "offering_id", "custom_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subs_offerings_app_id",
                table: "subs_offerings",
                column: "app_id");

            migrationBuilder.CreateIndex(
                name: "IX_subs_plan_changes_plan_change_id",
                table: "subs_plan_changes",
                column: "plan_change_id");

            migrationBuilder.CreateIndex(
                name: "IX_subs_plan_checkouts_expiration",
                table: "subs_plan_checkouts",
                column: "expiration");

            migrationBuilder.CreateIndex(
                name: "IX_subs_plan_checkouts_invoice_id",
                table: "subs_plan_checkouts",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_subs_plan_checkouts_plan_id",
                table: "subs_plan_checkouts",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_subs_plan_checkouts_subscriber_id",
                table: "subs_plan_checkouts",
                column: "subscriber_id");

            migrationBuilder.CreateIndex(
                name: "IX_subs_plans_offering_id",
                table: "subs_plans",
                column: "offering_id");

            migrationBuilder.CreateIndex(
                name: "IX_subs_plans_features_feature_id",
                table: "subs_plans_features",
                column: "feature_id");

            migrationBuilder.CreateIndex(
                name: "IX_subs_portal_sessions_expiration",
                table: "subs_portal_sessions",
                column: "expiration");

            migrationBuilder.CreateIndex(
                name: "IX_subs_portal_sessions_subscriber_id",
                table: "subs_portal_sessions",
                column: "subscriber_id");

            migrationBuilder.CreateIndex(
                name: "IX_subs_subscriber_credits_history_subscriber_id_created_at",
                table: "subs_subscriber_credits_history",
                columns: new[] { "subscriber_id", "created_at" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_subs_subscriber_credits_history_subscriber_id_currency",
                table: "subs_subscriber_credits_history",
                columns: new[] { "subscriber_id", "currency" });

            migrationBuilder.CreateIndex(
                name: "IX_subs_subscribers_customer_id",
                table: "subs_subscribers",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_subs_subscribers_new_plan_id",
                table: "subs_subscribers",
                column: "new_plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_subs_subscribers_offering_id_customer_id",
                table: "subs_subscribers",
                columns: new[] { "offering_id", "customer_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subs_subscribers_plan_id",
                table: "subs_subscribers",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscriber_invoices_invoice_id",
                table: "subscriber_invoices",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscriber_invoices_subscriber_id_created_at",
                table: "subscriber_invoices",
                columns: new[] { "subscriber_id", "created_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_email_rules_subs_offerings_offering_id",
                table: "email_rules",
                column: "offering_id",
                principalTable: "subs_offerings",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_email_rules_subs_offerings_offering_id",
                table: "email_rules");

            migrationBuilder.DropTable(
                name: "customers_identities");

            migrationBuilder.DropTable(
                name: "subs_plan_changes");

            migrationBuilder.DropTable(
                name: "subs_plan_checkouts");

            migrationBuilder.DropTable(
                name: "subs_plans_features");

            migrationBuilder.DropTable(
                name: "subs_portal_sessions");

            migrationBuilder.DropTable(
                name: "subs_subscriber_credits_history");

            migrationBuilder.DropTable(
                name: "subscriber_invoices");

            migrationBuilder.DropTable(
                name: "subs_features");

            migrationBuilder.DropTable(
                name: "subs_subscriber_credits");

            migrationBuilder.DropTable(
                name: "subs_subscribers");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "subs_plans");

            migrationBuilder.DropTable(
                name: "subs_offerings");

            migrationBuilder.DropIndex(
                name: "IX_email_rules_offering_id",
                table: "email_rules");

            migrationBuilder.DropColumn(
                name: "offering_id",
                table: "email_rules");
        }
    }
}
