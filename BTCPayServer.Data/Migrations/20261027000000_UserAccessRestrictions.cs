using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261027000000_UserAccessRestrictions")]
public partial class UserAccessRestrictions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "user_access_restrictions",
            columns: table => new
            {
                user_id = table.Column<string>(type: "text", nullable: false),
                type = table.Column<string>(type: "text", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_access_restrictions", x => new { x.user_id, x.type });
                table.ForeignKey(
                    name: "FK_user_access_restrictions_AspNetUsers_user_id",
                    column: x => x.user_id,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        // Permanent Identity lockouts did not record whether they came from an administrator or monetization.
        // Classify users whose current monetization data clearly denies access as monetization-restricted and
        // conservatively treat every other permanent lockout as administrative.
        migrationBuilder.Sql("""
            WITH permanent_lockouts AS (
                SELECT users."Id" AS user_id
                FROM "AspNetUsers" users
                WHERE users."LockoutEnd" >= '9999-12-31 23:59:58+00'::timestamptz
            ),
            monetization_lockouts AS (
                SELECT DISTINCT permanent_lockouts.user_id
                FROM permanent_lockouts
                JOIN "AspNetUsers" users ON users."Id" = permanent_lockouts.user_id
                JOIN "Settings" settings
                  ON settings."Id" = 'BTCPayServer.Plugins.Monetization.MonetizationSettings'
                JOIN customers_identities identity
                  ON identity.type = 'ApplicationUserId'
                 AND identity.value = permanent_lockouts.user_id
                JOIN subs_subscribers subscriber
                  ON subscriber.customer_id = identity.customer_id
                 AND subscriber.offering_id = settings."Value"->>'OfferingId'
                WHERE NOT users."BypassMonetization"
                  AND subscriber.suspension_reason IS DISTINCT FROM 'Account disabled by administrator'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "AspNetUserRoles" user_role
                      JOIN "AspNetRoles" role ON role."Id" = user_role."RoleId"
                      WHERE user_role."UserId" = permanent_lockouts.user_id
                        AND role."Name" = 'ServerAdmin'
                  )
                  AND (
                      NOT subscriber.active
                      OR NOT EXISTS (
                          SELECT 1
                          FROM subs_plans_features plan_feature
                          JOIN subs_features feature ON feature.id = plan_feature.feature_id
                          WHERE plan_feature.plan_id = subscriber.plan_id
                            AND feature.custom_id = 'can-access'
                      )
                  )
            )
            INSERT INTO user_access_restrictions (user_id, type, created_at)
            SELECT permanent_lockouts.user_id,
                   CASE WHEN monetization_lockouts.user_id IS NULL
                       THEN 'Administrative'
                       ELSE 'Monetization'
                   END,
                   NOW()
            FROM permanent_lockouts
            LEFT JOIN monetization_lockouts USING (user_id);

            UPDATE "AspNetUsers"
            SET "LockoutEnd" = NULL, "AccessFailedCount" = 0
            WHERE "LockoutEnd" >= '9999-12-31 23:59:58+00'::timestamptz;
            """);

        // The previous active-user calculation treated temporary Identity lockouts as inactive.
        // Recalculate every store using access restrictions as the source of account availability.
        migrationBuilder.Sql("""
            WITH active_users AS (
                SELECT stores."Id",
                       COUNT(DISTINCT users."Id") FILTER (WHERE restrictions.user_id IS NULL) AS active_users
                FROM "Stores" stores
                LEFT JOIN "UserStore" user_store ON user_store."StoreDataId" = stores."Id"
                LEFT JOIN "AspNetUsers" users ON users."Id" = user_store."ApplicationUserId"
                LEFT JOIN user_access_restrictions restrictions ON restrictions.user_id = users."Id"
                GROUP BY stores."Id"
            ),
            expected_disabled AS (
                SELECT stores."Id",
                       COALESCE(stores."StoreBlob"->'noActiveUser' = 'true'::jsonb, false) AS current_value,
                       active_users.active_users = 0 AS expected_value
                FROM active_users
                JOIN "Stores" stores ON stores."Id" = active_users."Id"
            )
            UPDATE "Stores" stores
            SET "StoreBlob" = jsonb_set(stores."StoreBlob", '{noActiveUser}', to_jsonb(expected_disabled.expected_value))
            FROM expected_disabled
            WHERE stores."Id" = expected_disabled."Id"
              AND expected_disabled.expected_value <> expected_disabled.current_value;
            """);
    }
}
