using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BTCPayServer.Migrations
{
    /// <inheritdoc />
    public partial class AppStuff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    RequiresEmailConfirmation = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresApproval = table.Column<bool>(type: "boolean", nullable: false),
                    Approved = table.Column<bool>(type: "boolean", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DisabledNotifications = table.Column<string>(type: "text", nullable: true),
                    Blob = table.Column<byte[]>(type: "bytea", nullable: true),
                    Blob2 = table.Column<string>(type: "JSONB", nullable: true),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OffchainTransactions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Blob = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OffchainTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PairingCodes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Facade = table.Column<string>(type: "text", nullable: true),
                    StoreDataId = table.Column<string>(type: "text", nullable: true),
                    Expiration = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: true),
                    SIN = table.Column<string>(type: "text", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TokenValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PairingCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayjoinLocks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayjoinLocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlannedTransactions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BroadcastAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Blob = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannedTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "JSONB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    DerivationStrategy = table.Column<string>(type: "text", nullable: true),
                    DerivationStrategies = table.Column<string>(type: "JSONB", nullable: true),
                    StoreName = table.Column<string>(type: "text", nullable: true),
                    SpeedPolicy = table.Column<int>(type: "integer", nullable: false),
                    StoreWebsite = table.Column<string>(type: "text", nullable: true),
                    StoreCertificate = table.Column<byte[]>(type: "bytea", nullable: true),
                    StoreBlob = table.Column<string>(type: "JSONB", nullable: true),
                    DefaultCrypto = table.Column<string>(type: "text", nullable: true),
                    Archived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WalletObjects",
                columns: table => new
                {
                    WalletId = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Id = table.Column<string>(type: "text", nullable: false),
                    Data = table.Column<string>(type: "JSONB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletObjects", x => new { x.WalletId, x.Type, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "Wallets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Blob = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Webhooks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    Blob = table.Column<byte[]>(type: "bytea", nullable: true),
                    Blob2 = table.Column<string>(type: "JSONB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Webhooks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppStorageItems",
                columns: table => new
                {
                    Key = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Value = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppStorageItems", x => new { x.Key, x.UserId });
                    table.ForeignKey(
                        name: "FK_AppStorageItems_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    RoleId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Fido2Credentials",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    ApplicationUserId = table.Column<string>(type: "text", nullable: true),
                    Blob = table.Column<byte[]>(type: "bytea", nullable: true),
                    Blob2 = table.Column<string>(type: "JSONB", nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Fido2Credentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Fido2Credentials_AspNetUsers_ApplicationUserId",
                        column: x => x.ApplicationUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Files",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: true),
                    StorageFileName = table.Column<string>(type: "text", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApplicationUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Files", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Files_AspNetUsers_ApplicationUserId",
                        column: x => x.ApplicationUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApplicationUserId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NotificationType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Seen = table.Column<bool>(type: "boolean", nullable: false),
                    Blob = table.Column<byte[]>(type: "bytea", nullable: true),
                    Blob2 = table.Column<string>(type: "JSONB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_AspNetUsers_ApplicationUserId",
                        column: x => x.ApplicationUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "U2FDevices",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    KeyHandle = table.Column<byte[]>(type: "bytea", nullable: false),
                    PublicKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    AttestationCert = table.Column<byte[]>(type: "bytea", nullable: false),
                    Counter = table.Column<int>(type: "integer", nullable: false),
                    ApplicationUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_U2FDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_U2FDevices_AspNetUsers_ApplicationUserId",
                        column: x => x.ApplicationUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    UserId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Blob = table.Column<byte[]>(type: "bytea", nullable: true),
                    Blob2 = table.Column<string>(type: "JSONB", nullable: true),
                    Label = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiKeys_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApiKeys_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Apps",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    StoreDataId = table.Column<string>(type: "text", nullable: true),
                    AppType = table.Column<string>(type: "text", nullable: true),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TagAllInvoices = table.Column<bool>(type: "boolean", nullable: false),
                    Settings = table.Column<string>(type: "JSONB", nullable: true),
                    Archived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Apps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Apps_Stores_StoreDataId",
                        column: x => x.StoreDataId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Forms",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    StoreId = table.Column<string>(type: "text", nullable: true),
                    Config = table.Column<string>(type: "JSONB", nullable: true),
                    Public = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Forms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Forms_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    Amount = table.Column<decimal>(type: "NUMERIC", nullable: true),
                    StoreDataId = table.Column<string>(type: "text", nullable: true),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Blob = table.Column<byte[]>(type: "bytea", nullable: true),
                    Blob2 = table.Column<string>(type: "JSONB", nullable: true),
                    ItemCode = table.Column<string>(type: "text", nullable: true),
                    OrderId = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    ExceptionStatus = table.Column<string>(type: "text", nullable: true),
                    CustomerEmail = table.Column<string>(type: "text", nullable: true),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_Stores_StoreDataId",
                        column: x => x.StoreDataId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LightningAddresses",
                columns: table => new
                {
                    Username = table.Column<string>(type: "text", nullable: false),
                    StoreDataId = table.Column<string>(type: "text", nullable: false),
                    Blob = table.Column<byte[]>(type: "bytea", nullable: true),
                    Blob2 = table.Column<string>(type: "JSONB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LightningAddresses", x => x.Username);
                    table.ForeignKey(
                        name: "FK_LightningAddresses_Stores_StoreDataId",
                        column: x => x.StoreDataId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PairedSINData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StoreDataId = table.Column<string>(type: "text", nullable: true),
                    Label = table.Column<string>(type: "text", nullable: true),
                    PairingTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SIN = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PairedSINData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PairedSINData_Stores_StoreDataId",
                        column: x => x.StoreDataId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValue: new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0))),
                    StoreDataId = table.Column<string>(type: "text", nullable: true),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Blob = table.Column<byte[]>(type: "bytea", nullable: true),
                    Blob2 = table.Column<string>(type: "JSONB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentRequests_Stores_StoreDataId",
                        column: x => x.StoreDataId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayoutProcessors",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<string>(type: "text", nullable: true),
                    PaymentMethod = table.Column<string>(type: "text", nullable: true),
                    Processor = table.Column<string>(type: "text", nullable: true),
                    Blob = table.Column<byte[]>(type: "bytea", nullable: true),
                    Blob2 = table.Column<string>(type: "JSONB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayoutProcessors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayoutProcessors_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PullPayments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    StartDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    Blob = table.Column<string>(type: "JSONB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PullPayments_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StoreDataId = table.Column<string>(type: "text", nullable: true),
                    Role = table.Column<string>(type: "text", nullable: true),
                    Permissions = table.Column<List<string>>(type: "text[]", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StoreRoles_Stores_StoreDataId",
                        column: x => x.StoreDataId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreSettings",
                columns: table => new
                {
                    Name = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "JSONB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreSettings", x => new { x.StoreId, x.Name });
                    table.ForeignKey(
                        name: "FK_StoreSettings_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WalletObjectLinks",
                columns: table => new
                {
                    WalletId = table.Column<string>(type: "text", nullable: false),
                    AType = table.Column<string>(type: "text", nullable: false),
                    AId = table.Column<string>(type: "text", nullable: false),
                    BType = table.Column<string>(type: "text", nullable: false),
                    BId = table.Column<string>(type: "text", nullable: false),
                    Data = table.Column<string>(type: "JSONB", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletObjectLinks", x => new { x.WalletId, x.AType, x.AId, x.BType, x.BId });
                    table.ForeignKey(
                        name: "FK_WalletObjectLinks_WalletObjects_WalletId_AType_AId",
                        columns: x => new { x.WalletId, x.AType, x.AId },
                        principalTable: "WalletObjects",
                        principalColumns: new[] { "WalletId", "Type", "Id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WalletObjectLinks_WalletObjects_WalletId_BType_BId",
                        columns: x => new { x.WalletId, x.BType, x.BId },
                        principalTable: "WalletObjects",
                        principalColumns: new[] { "WalletId", "Type", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WalletTransactions",
                columns: table => new
                {
                    WalletDataId = table.Column<string>(type: "text", nullable: false),
                    TransactionId = table.Column<string>(type: "text", nullable: false),
                    Labels = table.Column<string>(type: "text", nullable: true),
                    Blob = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTransactions", x => new { x.WalletDataId, x.TransactionId });
                    table.ForeignKey(
                        name: "FK_WalletTransactions_Wallets_WalletDataId",
                        column: x => x.WalletDataId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StoreWebhooks",
                columns: table => new
                {
                    StoreId = table.Column<string>(type: "text", nullable: false),
                    WebhookId = table.Column<string>(type: "character varying(25)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoreWebhooks", x => new { x.StoreId, x.WebhookId });
                    table.ForeignKey(
                        name: "FK_StoreWebhooks_Stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StoreWebhooks_Webhooks_WebhookId",
                        column: x => x.WebhookId,
                        principalTable: "Webhooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    WebhookId = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Blob = table.Column<string>(type: "JSONB", nullable: true),
                    Pruned = table.Column<bool>(type: "boolean", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "AddressInvoices",
                columns: table => new
                {
                    Address = table.Column<string>(type: "text", nullable: false),
                    InvoiceDataId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddressInvoices", x => x.Address);
                    table.ForeignKey(
                        name: "FK_AddressInvoices_Invoices_InvoiceDataId",
                        column: x => x.InvoiceDataId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceSearches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    InvoiceDataId = table.Column<string>(type: "text", nullable: true),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceSearches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceSearches_Invoices_InvoiceDataId",
                        column: x => x.InvoiceDataId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InvoiceDataId = table.Column<string>(type: "text", nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    Amount = table.Column<decimal>(type: "NUMERIC", nullable: true),
                    Blob = table.Column<byte[]>(type: "bytea", nullable: true),
                    Blob2 = table.Column<string>(type: "JSONB", nullable: true),
                    Type = table.Column<string>(type: "text", nullable: true),
                    Accounted = table.Column<bool>(type: "boolean", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_Invoices_InvoiceDataId",
                        column: x => x.InvoiceDataId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PendingInvoices",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingInvoices_Invoices_Id",
                        column: x => x.Id,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Payouts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PullPaymentDataId = table.Column<string>(type: "character varying(30)", nullable: true),
                    StoreDataId = table.Column<string>(type: "text", nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PayoutMethodId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Blob = table.Column<string>(type: "JSONB", nullable: true),
                    Proof = table.Column<string>(type: "JSONB", nullable: true),
                    Destination = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payouts_PullPayments_PullPaymentDataId",
                        column: x => x.PullPaymentDataId,
                        principalTable: "PullPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Payouts_Stores_StoreDataId",
                        column: x => x.StoreDataId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Refunds",
                columns: table => new
                {
                    InvoiceDataId = table.Column<string>(type: "text", nullable: false),
                    PullPaymentDataId = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Refunds", x => new { x.InvoiceDataId, x.PullPaymentDataId });
                    table.ForeignKey(
                        name: "FK_Refunds_Invoices_InvoiceDataId",
                        column: x => x.InvoiceDataId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Refunds_PullPayments_PullPaymentDataId",
                        column: x => x.PullPaymentDataId,
                        principalTable: "PullPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserStore",
                columns: table => new
                {
                    ApplicationUserId = table.Column<string>(type: "text", nullable: false),
                    StoreDataId = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserStore", x => new { x.ApplicationUserId, x.StoreDataId });
                    table.ForeignKey(
                        name: "FK_UserStore_AspNetUsers_ApplicationUserId",
                        column: x => x.ApplicationUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserStore_StoreRoles_Role",
                        column: x => x.Role,
                        principalTable: "StoreRoles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserStore_Stores_StoreDataId",
                        column: x => x.StoreDataId,
                        principalTable: "Stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceWebhookDeliveries",
                columns: table => new
                {
                    InvoiceId = table.Column<string>(type: "text", nullable: false),
                    DeliveryId = table.Column<string>(type: "character varying(25)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceWebhookDeliveries", x => new { x.InvoiceId, x.DeliveryId });
                    table.ForeignKey(
                        name: "FK_InvoiceWebhookDeliveries_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvoiceWebhookDeliveries_WebhookDeliveries_DeliveryId",
                        column: x => x.DeliveryId,
                        principalTable: "WebhookDeliveries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AddressInvoices_InvoiceDataId",
                table: "AddressInvoices",
                column: "InvoiceDataId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_StoreId",
                table: "ApiKeys",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_UserId",
                table: "ApiKeys",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Apps_StoreDataId",
                table: "Apps",
                column: "StoreDataId");

            migrationBuilder.CreateIndex(
                name: "IX_AppStorageItems_UserId",
                table: "AppStorageItems",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fido2Credentials_ApplicationUserId",
                table: "Fido2Credentials",
                column: "ApplicationUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Files_ApplicationUserId",
                table: "Files",
                column: "ApplicationUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Forms_StoreId",
                table: "Forms",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Created",
                table: "Invoices",
                column: "Created");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_OrderId",
                table: "Invoices",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_StoreDataId",
                table: "Invoices",
                column: "StoreDataId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceSearches_InvoiceDataId",
                table: "InvoiceSearches",
                column: "InvoiceDataId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceSearches_Value",
                table: "InvoiceSearches",
                column: "Value");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceWebhookDeliveries_DeliveryId",
                table: "InvoiceWebhookDeliveries",
                column: "DeliveryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceWebhookDeliveries_InvoiceId",
                table: "InvoiceWebhookDeliveries",
                column: "InvoiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LightningAddresses_StoreDataId",
                table: "LightningAddresses",
                column: "StoreDataId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ApplicationUserId",
                table: "Notifications",
                column: "ApplicationUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PairedSINData_SIN",
                table: "PairedSINData",
                column: "SIN");

            migrationBuilder.CreateIndex(
                name: "IX_PairedSINData_StoreDataId",
                table: "PairedSINData",
                column: "StoreDataId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_Status",
                table: "PaymentRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentRequests_StoreDataId",
                table: "PaymentRequests",
                column: "StoreDataId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_InvoiceDataId",
                table: "Payments",
                column: "InvoiceDataId");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutProcessors_StoreId",
                table: "PayoutProcessors",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_Destination_State",
                table: "Payouts",
                columns: new[] { "Destination", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_PullPaymentDataId",
                table: "Payouts",
                column: "PullPaymentDataId");

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_State",
                table: "Payouts",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_Payouts_StoreDataId",
                table: "Payouts",
                column: "StoreDataId");

            migrationBuilder.CreateIndex(
                name: "IX_PullPayments_StoreId",
                table: "PullPayments",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_PullPaymentDataId",
                table: "Refunds",
                column: "PullPaymentDataId");

            migrationBuilder.CreateIndex(
                name: "IX_StoreRoles_StoreDataId_Role",
                table: "StoreRoles",
                columns: new[] { "StoreDataId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreWebhooks_StoreId",
                table: "StoreWebhooks",
                column: "StoreId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoreWebhooks_WebhookId",
                table: "StoreWebhooks",
                column: "WebhookId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_U2FDevices_ApplicationUserId",
                table: "U2FDevices",
                column: "ApplicationUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserStore_Role",
                table: "UserStore",
                column: "Role");

            migrationBuilder.CreateIndex(
                name: "IX_UserStore_StoreDataId",
                table: "UserStore",
                column: "StoreDataId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletObjectLinks_WalletId_BType_BId",
                table: "WalletObjectLinks",
                columns: new[] { "WalletId", "BType", "BId" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletObjects_Type_Id",
                table: "WalletObjects",
                columns: new[] { "Type", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_Timestamp",
                table: "WebhookDeliveries",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_WebhookId",
                table: "WebhookDeliveries",
                column: "WebhookId");

            migrationBuilder.Sql("CREATE FUNCTION \"LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA\"() RETURNS trigger as $LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA$\r\nBEGIN\r\n  DELETE FROM \"AppStorageItems\"\r\n  WHERE NEW.\"UserId\" = \"AppStorageItems\".\"UserId\" AND NEW.\"Key\" = \"AppStorageItems\".\"Key\" AND NEW.\"Version\" > \"AppStorageItems\".\"Version\";\r\nRETURN NEW;\r\nEND;\r\n$LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA$ LANGUAGE plpgsql;\r\nCREATE TRIGGER LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA BEFORE INSERT\r\nON \"AppStorageItems\"\r\nFOR EACH ROW EXECUTE PROCEDURE \"LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA\"();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION \"LC_TRIGGER_BEFORE_INSERT_APPSTORAGEITEMDATA\"() CASCADE;");

            migrationBuilder.DropTable(
                name: "AddressInvoices");

            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.DropTable(
                name: "Apps");

            migrationBuilder.DropTable(
                name: "AppStorageItems");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "Fido2Credentials");

            migrationBuilder.DropTable(
                name: "Files");

            migrationBuilder.DropTable(
                name: "Forms");

            migrationBuilder.DropTable(
                name: "InvoiceSearches");

            migrationBuilder.DropTable(
                name: "InvoiceWebhookDeliveries");

            migrationBuilder.DropTable(
                name: "LightningAddresses");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "OffchainTransactions");

            migrationBuilder.DropTable(
                name: "PairedSINData");

            migrationBuilder.DropTable(
                name: "PairingCodes");

            migrationBuilder.DropTable(
                name: "PayjoinLocks");

            migrationBuilder.DropTable(
                name: "PaymentRequests");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "PayoutProcessors");

            migrationBuilder.DropTable(
                name: "Payouts");

            migrationBuilder.DropTable(
                name: "PendingInvoices");

            migrationBuilder.DropTable(
                name: "PlannedTransactions");

            migrationBuilder.DropTable(
                name: "Refunds");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "StoreSettings");

            migrationBuilder.DropTable(
                name: "StoreWebhooks");

            migrationBuilder.DropTable(
                name: "U2FDevices");

            migrationBuilder.DropTable(
                name: "UserStore");

            migrationBuilder.DropTable(
                name: "WalletObjectLinks");

            migrationBuilder.DropTable(
                name: "WalletTransactions");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "WebhookDeliveries");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "PullPayments");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "StoreRoles");

            migrationBuilder.DropTable(
                name: "WalletObjects");

            migrationBuilder.DropTable(
                name: "Wallets");

            migrationBuilder.DropTable(
                name: "Webhooks");

            migrationBuilder.DropTable(
                name: "Stores");
        }
    }
}
