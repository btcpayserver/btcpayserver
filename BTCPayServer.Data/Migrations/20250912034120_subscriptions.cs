using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BTCPayServer.Migrations
{
    /// <inheritdoc />
    public partial class subscriptions : Migration
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
                    email = table.Column<string>(type: "text", nullable: true),
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
                    store_id = table.Column<string>(type: "text", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    additional_data = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions_offerings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subscriptions_offerings_Stores_store_id",
                        column: x => x.store_id,
                        principalTable: "Stores",
                        principalColumn: "Id",
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
                    description = table.Column<string>(type: "text", nullable: true),
                    allow_upgrade = table.Column<bool>(type: "boolean", nullable: false),
                    members_count = table.Column<int>(type: "integer", nullable: false),
                    renewable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
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

            migrationBuilder.CreateIndex(
                name: "IX_customers_store_id_email",
                table: "customers",
                columns: new[] { "store_id", "email" },
                unique: true);

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
                name: "IX_subscriptions_offerings_store_id",
                table: "subscriptions_offerings",
                column: "store_id");

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
