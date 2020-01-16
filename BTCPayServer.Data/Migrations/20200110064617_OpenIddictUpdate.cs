using System;
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20200110064617_OpenIddictUpdate")]
    public partial class OpenIddictUpdate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (!migrationBuilder.IsSqlite())
            {
                migrationBuilder.AlterColumn<string>(
                    name: "Subject",
                    table: "OpenIddictTokens",
                    maxLength: 450,
                    nullable: true,
                    oldClrType: typeof(string),
                    oldMaxLength: 450);

                migrationBuilder.AlterColumn<string>(
                    name: "Subject",
                    table: "OpenIddictAuthorizations",
                    maxLength: 450,
                    nullable: true,
                    oldClrType: typeof(string),
                    oldMaxLength: 450);
            }

            else
            {
                ReplaceOldTable(migrationBuilder, s =>
                {
                    migrationBuilder.CreateTable(
                        name: s,
                        columns: table => new
                        {
                            ApplicationId = table.Column<string>(nullable: true, maxLength: null),
                            AuthorizationId = table.Column<string>(nullable: true, maxLength: null),
                            ConcurrencyToken = table.Column<string>(maxLength: 50, nullable: true),
                            CreationDate = table.Column<DateTimeOffset>(nullable: true),
                            ExpirationDate = table.Column<DateTimeOffset>(nullable: true),
                            Id = table.Column<string>(nullable: false, maxLength: null),
                            Payload = table.Column<string>(nullable: true),
                            Properties = table.Column<string>(nullable: true),
                            ReferenceId = table.Column<string>(maxLength: 100, nullable: true),
                            Status = table.Column<string>(maxLength: 25, nullable: false),
                            Subject = table.Column<string>(maxLength: 450, nullable: true),
                            Type = table.Column<string>(maxLength: 25, nullable: false)
                        },
                        constraints: table =>
                        {
                            table.PrimaryKey("PK_OpenIddictTokens", x => x.Id);
                            table.ForeignKey(
                                name: "FK_OpenIddictTokens_OpenIddictApplications_ApplicationId",
                                column: x => x.ApplicationId,
                                principalTable: "OpenIddictApplications",
                                principalColumn: "Id",
                                onDelete: ReferentialAction.Restrict);
                            table.ForeignKey(
                                name: "FK_OpenIddictTokens_OpenIddictAuthorizations_AuthorizationId",
                                column: x => x.AuthorizationId,
                                principalTable: "OpenIddictAuthorizations",
                                principalColumn: "Id",
                                onDelete: ReferentialAction.Restrict);
                        });
                }, "OpenIddictTokens");

                ReplaceOldTable(migrationBuilder, s =>
                {
                    migrationBuilder.CreateTable(
                        name: s,
                        columns: table => new
                        {
                            ApplicationId = table.Column<string>(nullable: true, maxLength: null),
                            ConcurrencyToken = table.Column<string>(maxLength: 50, nullable: true),
                            Id = table.Column<string>(nullable: false, maxLength: null),
                            Properties = table.Column<string>(nullable: true),
                            Scopes = table.Column<string>(nullable: true),
                            Status = table.Column<string>(maxLength: 25, nullable: false),
                            Subject = table.Column<string>(maxLength: 450, nullable: true),
                            Type = table.Column<string>(maxLength: 25, nullable: false)
                        },
                        constraints: table =>
                        {
                            table.PrimaryKey("PK_OpenIddictAuthorizations", x => x.Id);
                            table.ForeignKey(
                                name: "FK_OpenIddictAuthorizations_OpenIddictApplications_ApplicationId",
                                column: x => x.ApplicationId,
                                principalTable: "OpenIddictApplications",
                                principalColumn: "Id",
                                onDelete: ReferentialAction.Restrict);
                        });
                }, "OpenIddictAuthorizations");
            }

            migrationBuilder.AddColumn<string>(
                name: "Requirements",
                table: "OpenIddictApplications",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (!migrationBuilder.IsSqlite())
            {
                migrationBuilder.DropColumn(
                    name: "Requirements",
                    table: "OpenIddictApplications");

                migrationBuilder.AlterColumn<string>(
                    name: "Subject",
                    table: "OpenIddictTokens",
                    maxLength: 450,
                    nullable: false,
                    oldClrType: typeof(string),
                    oldMaxLength: 450,
                    oldNullable: true);

                migrationBuilder.AlterColumn<string>(
                    name: "Subject",
                    table: "OpenIddictAuthorizations",
                    maxLength: 450,
                    nullable: false,
                    oldClrType: typeof(string),
                    oldMaxLength: 450,
                    oldNullable: true);
            }
            else
            {
                ReplaceOldTable(migrationBuilder, s =>
                {
                    migrationBuilder.CreateTable(
                        name: s,
                        columns: table => new
                        {
                            ApplicationId = table.Column<string>(nullable: true, maxLength: null),
                            AuthorizationId = table.Column<string>(nullable: true, maxLength: null),
                            ConcurrencyToken = table.Column<string>(maxLength: 50, nullable: true),
                            CreationDate = table.Column<DateTimeOffset>(nullable: true),
                            ExpirationDate = table.Column<DateTimeOffset>(nullable: true),
                            Id = table.Column<string>(nullable: false, maxLength: null),
                            Payload = table.Column<string>(nullable: true),
                            Properties = table.Column<string>(nullable: true),
                            ReferenceId = table.Column<string>(maxLength: 100, nullable: true),
                            Status = table.Column<string>(maxLength: 25, nullable: false),
                            Subject = table.Column<string>(maxLength: 450, nullable: false),
                            Type = table.Column<string>(maxLength: 25, nullable: false)
                        },
                        constraints: table =>
                        {
                            table.PrimaryKey("PK_OpenIddictTokens", x => x.Id);
                            table.ForeignKey(
                                name: "FK_OpenIddictTokens_OpenIddictApplications_ApplicationId",
                                column: x => x.ApplicationId,
                                principalTable: "OpenIddictApplications",
                                principalColumn: "Id",
                                onDelete: ReferentialAction.Restrict);
                            table.ForeignKey(
                                name: "FK_OpenIddictTokens_OpenIddictAuthorizations_AuthorizationId",
                                column: x => x.AuthorizationId,
                                principalTable: "OpenIddictAuthorizations",
                                principalColumn: "Id",
                                onDelete: ReferentialAction.Restrict);
                        });
                }, "OpenIddictTokens", "WHERE Subject IS NOT NULL");

                ReplaceOldTable(migrationBuilder, s =>
                {
                    migrationBuilder.CreateTable(
                        name: s,
                        columns: table => new
                        {
                            ApplicationId = table.Column<string>(nullable: true, maxLength: null),
                            ConcurrencyToken = table.Column<string>(maxLength: 50, nullable: true),
                            Id = table.Column<string>(nullable: false, maxLength: null),
                            Properties = table.Column<string>(nullable: true),
                            Scopes = table.Column<string>(nullable: true),
                            Status = table.Column<string>(maxLength: 25, nullable: false),
                            Subject = table.Column<string>(maxLength: 450, nullable: false),
                            Type = table.Column<string>(maxLength: 25, nullable: false)
                        },
                        constraints: table =>
                        {
                            table.PrimaryKey("PK_OpenIddictAuthorizations", x => x.Id);
                            table.ForeignKey(
                                name: "FK_OpenIddictAuthorizations_OpenIddictApplications_ApplicationId",
                                column: x => x.ApplicationId,
                                principalTable: "OpenIddictApplications",
                                principalColumn: "Id",
                                onDelete: ReferentialAction.Restrict);
                        });
                }, "OpenIddictAuthorizations", "WHERE Subject IS NOT NULL");

                ReplaceOldTable(migrationBuilder, s =>
                    {
                        migrationBuilder.CreateTable(
                            name: s,
                            columns: table => new
                            {
                                ClientId = table.Column<string>(maxLength: 100, nullable: false),
                                ClientSecret = table.Column<string>(nullable: true),
                                ConcurrencyToken = table.Column<string>(maxLength: 50, nullable: true),
                                ConsentType = table.Column<string>(nullable: true),
                                DisplayName = table.Column<string>(nullable: true),
                                Id = table.Column<string>(nullable: false, maxLength: null),
                                Permissions = table.Column<string>(nullable: true),
                                PostLogoutRedirectUris = table.Column<string>(nullable: true),
                                Properties = table.Column<string>(nullable: true),
                                RedirectUris = table.Column<string>(nullable: true),
                                Type = table.Column<string>(maxLength: 25, nullable: false),
                                ApplicationUserId = table.Column<string>(nullable: true, maxLength: null)
                            },
                            constraints: table =>
                            {
                                table.PrimaryKey("PK_OpenIddictApplications", x => x.Id);
                                table.ForeignKey(
                                    name: "FK_OpenIddictApplications_AspNetUsers_ApplicationUserId",
                                    column: x => x.ApplicationUserId,
                                    principalTable: "AspNetUsers",
                                    principalColumn: "Id",
                                    onDelete: ReferentialAction.Restrict);
                            });
                    }, "OpenIddictApplications", "",
                    "ClientId, ClientSecret, ConcurrencyToken, ConsentType, DisplayName, Id, Permissions, PostLogoutRedirectUris, Properties, RedirectUris, Type, ApplicationUserId");
            }
        }

        private void ReplaceOldTable(MigrationBuilder migrationBuilder, Action<string> createTable, string tableName,
            string whereClause = "", string columns = "*")
        {
            createTable.Invoke($"New_{tableName}");
            migrationBuilder.Sql(
                $"INSERT INTO New_{tableName} {(columns == "*" ? string.Empty : $"({columns})")}SELECT {columns} FROM {tableName} {whereClause};");
            migrationBuilder.Sql("PRAGMA foreign_keys=\"0\"", true);
            migrationBuilder.Sql($"DROP TABLE {tableName}", true);
            migrationBuilder.Sql($"ALTER TABLE New_{tableName} RENAME TO {tableName}", true);
            migrationBuilder.Sql("PRAGMA foreign_keys=\"1\"", true);
        }
    }
}
