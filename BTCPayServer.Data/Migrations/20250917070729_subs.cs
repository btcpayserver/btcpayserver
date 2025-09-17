using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BTCPayServer.Migrations
{
    /// <inheritdoc />
    public partial class subs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "subscriptions_offerings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    app_id = table.Column<string>(type: "text", nullable: false),
                    success_redirect_url = table.Column<string>(type: "text", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    additional_data = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions_offerings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subscriptions_offerings_Apps_app_id",
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
                name: "subscriptions_entitlements",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    name = table.Column<string>(type: "text", nullable: true),
                    CustomId = table.Column<string>(type: "text", nullable: false),
                    offering_id = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions_entitlements", x => x.id);
                    table.ForeignKey(
                        name: "FK_subscriptions_entitlements_subscriptions_offerings_offering~",
                        column: x => x.offering_id,
                        principalTable: "subscriptions_offerings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions_plans",
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
                    allow_upgrade = table.Column<bool>(type: "boolean", nullable: false),
                    members_count = table.Column<int>(type: "integer", nullable: false),
                    optimistic_activation = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    additional_data = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions_plans", x => x.id);
                    table.ForeignKey(
                        name: "FK_subscriptions_plans_subscriptions_offerings_offering_id",
                        column: x => x.offering_id,
                        principalTable: "subscriptions_offerings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions_plans_entitlements",
                columns: table => new
                {
                    plan_id = table.Column<string>(type: "text", nullable: false),
                    entitlement_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions_plans_entitlements", x => new { x.plan_id, x.entitlement_id });
                    table.ForeignKey(
                        name: "FK_subscriptions_plans_entitlements_subscriptions_entitlements~",
                        column: x => x.entitlement_id,
                        principalTable: "subscriptions_entitlements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_subscriptions_plans_entitlements_subscriptions_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "subscriptions_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions_subscribers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    offering_id = table.Column<string>(type: "text", nullable: false),
                    customer_id = table.Column<string>(type: "text", nullable: false),
                    plan_id = table.Column<string>(type: "text", nullable: false),
                    phase = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'Expired'::TEXT"),
                    period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    trial_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    grace_period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    canceled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    force_disabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    additional_data = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions_subscribers", x => x.id);
                    table.ForeignKey(
                        name: "FK_subscriptions_subscribers_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_subscriptions_subscribers_subscriptions_offerings_offering_~",
                        column: x => x.offering_id,
                        principalTable: "subscriptions_offerings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_subscriptions_subscribers_subscriptions_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "subscriptions_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions_plan_checkouts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    invoice_id = table.Column<string>(type: "text", nullable: true),
                    success_redirect_url = table.Column<string>(type: "text", nullable: true),
                    is_trial = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    plan_id = table.Column<string>(type: "text", nullable: false),
                    new_subscriber = table.Column<bool>(type: "boolean", nullable: false),
                    subscriber_id = table.Column<long>(type: "bigint", nullable: true),
                    invoice_metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    new_subscriber_metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    metadata = table.Column<string>(type: "jsonb", nullable: false),
                    additional_data = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions_plan_checkouts", x => x.id);
                    table.ForeignKey(
                        name: "FK_subscriptions_plan_checkouts_Invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_subscriptions_plan_checkouts_subscriptions_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "subscriptions_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_subscriptions_plan_checkouts_subscriptions_subscribers_subs~",
                        column: x => x.subscriber_id,
                        principalTable: "subscriptions_subscribers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customers_store_id_external_ref",
                table: "customers",
                columns: new[] { "store_id", "external_ref" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_entitlements_offering_id_CustomId",
                table: "subscriptions_entitlements",
                columns: new[] { "offering_id", "CustomId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_offerings_app_id",
                table: "subscriptions_offerings",
                column: "app_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_plan_checkouts_invoice_id",
                table: "subscriptions_plan_checkouts",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_plan_checkouts_plan_id",
                table: "subscriptions_plan_checkouts",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_plan_checkouts_subscriber_id",
                table: "subscriptions_plan_checkouts",
                column: "subscriber_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_plans_offering_id",
                table: "subscriptions_plans",
                column: "offering_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_plans_entitlements_entitlement_id",
                table: "subscriptions_plans_entitlements",
                column: "entitlement_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_subscribers_customer_id",
                table: "subscriptions_subscribers",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_subscribers_offering_id_customer_id",
                table: "subscriptions_subscribers",
                columns: new[] { "offering_id", "customer_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_subscribers_plan_id",
                table: "subscriptions_subscribers",
                column: "plan_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customers_identities");

            migrationBuilder.DropTable(
                name: "subscriptions_plan_checkouts");

            migrationBuilder.DropTable(
                name: "subscriptions_plans_entitlements");

            migrationBuilder.DropTable(
                name: "subscriptions_subscribers");

            migrationBuilder.DropTable(
                name: "subscriptions_entitlements");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "subscriptions_plans");

            migrationBuilder.DropTable(
                name: "subscriptions_offerings");
        }
    }
}
