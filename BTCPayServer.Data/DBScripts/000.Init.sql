CREATE TABLE "AddressInvoices" (
    "Address" text NOT NULL,
    "InvoiceDataId" text,
    "CreatedTime" timestamp with time zone
);
CREATE TABLE "ApiKeys" (
    "Id" character varying(50) NOT NULL,
    "StoreId" character varying(50),
    "Permissions" text,
    "Type" integer DEFAULT 0 NOT NULL,
    "UserId" character varying(50),
    "Label" text
);
CREATE TABLE "Apps" (
    "Id" text NOT NULL,
    "AppType" text,
    "Created" timestamp with time zone NOT NULL,
    "Name" text,
    "Settings" text,
    "StoreDataId" text,
    "TagAllInvoices" boolean DEFAULT false NOT NULL
);
CREATE TABLE "AspNetRoleClaims" (
    "Id" integer NOT NULL,
    "ClaimType" text,
    "ClaimValue" text,
    "RoleId" text NOT NULL
);
CREATE TABLE "AspNetRoles" (
    "Id" text NOT NULL,
    "ConcurrencyStamp" text,
    "Name" character varying(256),
    "NormalizedName" character varying(256)
);
CREATE TABLE "AspNetUserClaims" (
    "Id" integer NOT NULL,
    "ClaimType" text,
    "ClaimValue" text,
    "UserId" text NOT NULL
);
CREATE TABLE "AspNetUserLogins" (
    "LoginProvider" character varying(255) NOT NULL,
    "ProviderKey" character varying(255) NOT NULL,
    "ProviderDisplayName" text,
    "UserId" text NOT NULL
);
CREATE TABLE "AspNetUserRoles" (
    "UserId" text NOT NULL,
    "RoleId" text NOT NULL
);
CREATE TABLE "AspNetUserTokens" (
    "UserId" text NOT NULL,
    "LoginProvider" character varying(64) NOT NULL,
    "Name" character varying(64) NOT NULL,
    "Value" text
);
CREATE TABLE "AspNetUsers" (
    "Id" text NOT NULL,
    "AccessFailedCount" integer NOT NULL,
    "ConcurrencyStamp" text,
    "Email" character varying(256),
    "EmailConfirmed" boolean NOT NULL,
    "LockoutEnabled" boolean NOT NULL,
    "LockoutEnd" timestamp with time zone,
    "NormalizedEmail" character varying(256),
    "NormalizedUserName" character varying(256),
    "PasswordHash" text,
    "PhoneNumber" text,
    "PhoneNumberConfirmed" boolean NOT NULL,
    "SecurityStamp" text,
    "TwoFactorEnabled" boolean NOT NULL,
    "UserName" character varying(256),
    "RequiresEmailConfirmation" boolean DEFAULT false NOT NULL
);
CREATE TABLE "Files" (
    "Id" text NOT NULL,
    "FileName" text,
    "StorageFileName" text,
    "Timestamp" timestamp with time zone NOT NULL,
    "ApplicationUserId" text
);
CREATE TABLE "HistoricalAddressInvoices" (
    "InvoiceDataId" text NOT NULL,
    "Address" text NOT NULL,
    "Assigned" timestamp with time zone NOT NULL,
    "UnAssigned" timestamp with time zone,
    "CryptoCode" text
);
CREATE TABLE "InvoiceEvents" (
    "InvoiceDataId" text NOT NULL,
    "UniqueId" text NOT NULL,
    "Message" text,
    "Timestamp" timestamp with time zone NOT NULL
);
CREATE TABLE "Invoices" (
    "Id" text NOT NULL,
    "Blob" bytea,
    "Created" timestamp with time zone NOT NULL,
    "CustomerEmail" text,
    "ExceptionStatus" text,
    "ItemCode" text,
    "OrderId" text,
    "Status" text,
    "StoreDataId" text
);
CREATE TABLE "PairedSINData" (
    "Id" text NOT NULL,
    "Label" text,
    "PairingTime" timestamp with time zone NOT NULL,
    "SIN" text,
    "StoreDataId" text
);
CREATE TABLE "PairingCodes" (
    "Id" text NOT NULL,
    "DateCreated" timestamp with time zone NOT NULL,
    "Expiration" timestamp with time zone NOT NULL,
    "Facade" text,
    "Label" text,
    "SIN" text,
    "StoreDataId" text,
    "TokenValue" text
);
CREATE TABLE "PaymentRequests" (
    "Id" text NOT NULL,
    "StoreDataId" text,
    "Status" integer NOT NULL,
    "Blob" bytea,
    "Created" timestamp with time zone DEFAULT '1970-01-01 00:00:00+00'::timestamp with time zone NOT NULL
);
CREATE TABLE "Payments" (
    "Id" text NOT NULL,
    "Blob" bytea,
    "InvoiceDataId" text,
    "Accounted" boolean DEFAULT false NOT NULL
);
CREATE TABLE "PendingInvoices" (
    "Id" text NOT NULL
);
CREATE TABLE "RefundAddresses" (
    "Id" text NOT NULL,
    "Blob" bytea,
    "InvoiceDataId" text
);
CREATE TABLE "Settings" (
    "Id" text NOT NULL,
    "Value" text
);
CREATE TABLE "Stores" (
    "Id" text NOT NULL,
    "DerivationStrategy" text,
    "SpeedPolicy" integer NOT NULL,
    "StoreCertificate" bytea,
    "StoreName" text,
    "StoreWebsite" text,
    "StoreBlob" bytea,
    "DerivationStrategies" text,
    "DefaultCrypto" text
);
CREATE TABLE "U2FDevices" (
    "Id" text NOT NULL,
    "Name" text,
    "KeyHandle" bytea NOT NULL,
    "PublicKey" bytea NOT NULL,
    "AttestationCert" bytea NOT NULL,
    "Counter" integer NOT NULL,
    "ApplicationUserId" text
);
CREATE TABLE "UserStore" (
    "ApplicationUserId" text NOT NULL,
    "StoreDataId" text NOT NULL,
    "Role" text
);
CREATE TABLE "WalletTransactions" (
    "WalletDataId" text NOT NULL,
    "TransactionId" text NOT NULL,
    "Labels" text,
    "Blob" bytea
);
CREATE TABLE "Wallets" (
    "Id" text NOT NULL,
    "Blob" bytea
);
ALTER TABLE ONLY "AddressInvoices"
    ADD CONSTRAINT "PK_AddressInvoices" PRIMARY KEY ("Address");
ALTER TABLE ONLY "ApiKeys"
    ADD CONSTRAINT "PK_ApiKeys" PRIMARY KEY ("Id");
ALTER TABLE ONLY "Apps"
    ADD CONSTRAINT "PK_Apps" PRIMARY KEY ("Id");
ALTER TABLE ONLY "AspNetRoleClaims"
    ADD CONSTRAINT "PK_AspNetRoleClaims" PRIMARY KEY ("Id");
ALTER TABLE ONLY "AspNetRoles"
    ADD CONSTRAINT "PK_AspNetRoles" PRIMARY KEY ("Id");
ALTER TABLE ONLY "AspNetUserClaims"
    ADD CONSTRAINT "PK_AspNetUserClaims" PRIMARY KEY ("Id");
ALTER TABLE ONLY "AspNetUserLogins"
    ADD CONSTRAINT "PK_AspNetUserLogins" PRIMARY KEY ("LoginProvider", "ProviderKey");
ALTER TABLE ONLY "AspNetUserRoles"
    ADD CONSTRAINT "PK_AspNetUserRoles" PRIMARY KEY ("UserId", "RoleId");
ALTER TABLE ONLY "AspNetUserTokens"
    ADD CONSTRAINT "PK_AspNetUserTokens" PRIMARY KEY ("UserId", "LoginProvider", "Name");
ALTER TABLE ONLY "AspNetUsers"
    ADD CONSTRAINT "PK_AspNetUsers" PRIMARY KEY ("Id");
ALTER TABLE ONLY "Files"
    ADD CONSTRAINT "PK_Files" PRIMARY KEY ("Id");
ALTER TABLE ONLY "HistoricalAddressInvoices"
    ADD CONSTRAINT "PK_HistoricalAddressInvoices" PRIMARY KEY ("InvoiceDataId", "Address");
ALTER TABLE ONLY "InvoiceEvents"
    ADD CONSTRAINT "PK_InvoiceEvents" PRIMARY KEY ("InvoiceDataId", "UniqueId");
ALTER TABLE ONLY "Invoices"
    ADD CONSTRAINT "PK_Invoices" PRIMARY KEY ("Id");
ALTER TABLE ONLY "PairedSINData"
    ADD CONSTRAINT "PK_PairedSINData" PRIMARY KEY ("Id");
ALTER TABLE ONLY "PairingCodes"
    ADD CONSTRAINT "PK_PairingCodes" PRIMARY KEY ("Id");
ALTER TABLE ONLY "PaymentRequests"
    ADD CONSTRAINT "PK_PaymentRequests" PRIMARY KEY ("Id");
ALTER TABLE ONLY "Payments"
    ADD CONSTRAINT "PK_Payments" PRIMARY KEY ("Id");
ALTER TABLE ONLY "PendingInvoices"
    ADD CONSTRAINT "PK_PendingInvoices" PRIMARY KEY ("Id");
ALTER TABLE ONLY "RefundAddresses"
    ADD CONSTRAINT "PK_RefundAddresses" PRIMARY KEY ("Id");
ALTER TABLE ONLY "Settings"
    ADD CONSTRAINT "PK_Settings" PRIMARY KEY ("Id");
ALTER TABLE ONLY "Stores"
    ADD CONSTRAINT "PK_Stores" PRIMARY KEY ("Id");
ALTER TABLE ONLY "U2FDevices"
    ADD CONSTRAINT "PK_U2FDevices" PRIMARY KEY ("Id");
ALTER TABLE ONLY "UserStore"
    ADD CONSTRAINT "PK_UserStore" PRIMARY KEY ("ApplicationUserId", "StoreDataId");
ALTER TABLE ONLY "WalletTransactions"
    ADD CONSTRAINT "PK_WalletTransactions" PRIMARY KEY ("WalletDataId", "TransactionId");
ALTER TABLE ONLY "Wallets"
    ADD CONSTRAINT "PK_Wallets" PRIMARY KEY ("Id");
CREATE INDEX "EmailIndex" ON "AspNetUsers" USING btree ("NormalizedEmail");
CREATE INDEX "IX_AddressInvoices_InvoiceDataId" ON "AddressInvoices" USING btree ("InvoiceDataId");
CREATE INDEX "IX_ApiKeys_StoreId" ON "ApiKeys" USING btree ("StoreId");
CREATE INDEX "IX_ApiKeys_UserId" ON "ApiKeys" USING btree ("UserId");
CREATE INDEX "IX_Apps_StoreDataId" ON "Apps" USING btree ("StoreDataId");
CREATE INDEX "IX_AspNetRoleClaims_RoleId" ON "AspNetRoleClaims" USING btree ("RoleId");
CREATE INDEX "IX_AspNetUserClaims_UserId" ON "AspNetUserClaims" USING btree ("UserId");
CREATE INDEX "IX_AspNetUserLogins_UserId" ON "AspNetUserLogins" USING btree ("UserId");
CREATE INDEX "IX_AspNetUserRoles_RoleId" ON "AspNetUserRoles" USING btree ("RoleId");
CREATE INDEX "IX_Files_ApplicationUserId" ON "Files" USING btree ("ApplicationUserId");
CREATE INDEX "IX_Invoices_StoreDataId" ON "Invoices" USING btree ("StoreDataId");
CREATE INDEX "IX_PairedSINData_SIN" ON "PairedSINData" USING btree ("SIN");
CREATE INDEX "IX_PairedSINData_StoreDataId" ON "PairedSINData" USING btree ("StoreDataId");
CREATE INDEX "IX_PaymentRequests_Status" ON "PaymentRequests" USING btree ("Status");
CREATE INDEX "IX_PaymentRequests_StoreDataId" ON "PaymentRequests" USING btree ("StoreDataId");
CREATE INDEX "IX_Payments_InvoiceDataId" ON "Payments" USING btree ("InvoiceDataId");
CREATE INDEX "IX_RefundAddresses_InvoiceDataId" ON "RefundAddresses" USING btree ("InvoiceDataId");
CREATE INDEX "IX_U2FDevices_ApplicationUserId" ON "U2FDevices" USING btree ("ApplicationUserId");
CREATE INDEX "IX_UserStore_StoreDataId" ON "UserStore" USING btree ("StoreDataId");
CREATE UNIQUE INDEX "RoleNameIndex" ON "AspNetRoles" USING btree ("NormalizedName");
CREATE UNIQUE INDEX "UserNameIndex" ON "AspNetUsers" USING btree ("NormalizedUserName");
ALTER TABLE ONLY "AddressInvoices"
    ADD CONSTRAINT "FK_AddressInvoices_Invoices_InvoiceDataId" FOREIGN KEY ("InvoiceDataId") REFERENCES "Invoices"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "ApiKeys"
    ADD CONSTRAINT "FK_ApiKeys_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "ApiKeys"
    ADD CONSTRAINT "FK_ApiKeys_Stores_StoreId" FOREIGN KEY ("StoreId") REFERENCES "Stores"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "Apps"
    ADD CONSTRAINT "FK_Apps_Stores_StoreDataId" FOREIGN KEY ("StoreDataId") REFERENCES "Stores"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "AspNetRoleClaims"
    ADD CONSTRAINT "FK_AspNetRoleClaims_AspNetRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "AspNetUserClaims"
    ADD CONSTRAINT "FK_AspNetUserClaims_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "AspNetUserLogins"
    ADD CONSTRAINT "FK_AspNetUserLogins_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "AspNetUserRoles"
    ADD CONSTRAINT "FK_AspNetUserRoles_AspNetRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "AspNetUserRoles"
    ADD CONSTRAINT "FK_AspNetUserRoles_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "AspNetUserTokens"
    ADD CONSTRAINT "FK_AspNetUserTokens_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "Files"
    ADD CONSTRAINT "FK_Files_AspNetUsers_ApplicationUserId" FOREIGN KEY ("ApplicationUserId") REFERENCES "AspNetUsers"("Id") ON DELETE RESTRICT;
ALTER TABLE ONLY "HistoricalAddressInvoices"
    ADD CONSTRAINT "FK_HistoricalAddressInvoices_Invoices_InvoiceDataId" FOREIGN KEY ("InvoiceDataId") REFERENCES "Invoices"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "InvoiceEvents"
    ADD CONSTRAINT "FK_InvoiceEvents_Invoices_InvoiceDataId" FOREIGN KEY ("InvoiceDataId") REFERENCES "Invoices"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "Invoices"
    ADD CONSTRAINT "FK_Invoices_Stores_StoreDataId" FOREIGN KEY ("StoreDataId") REFERENCES "Stores"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "PairedSINData"
    ADD CONSTRAINT "FK_PairedSINData_Stores_StoreDataId" FOREIGN KEY ("StoreDataId") REFERENCES "Stores"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "PaymentRequests"
    ADD CONSTRAINT "FK_PaymentRequests_Stores_StoreDataId" FOREIGN KEY ("StoreDataId") REFERENCES "Stores"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "Payments"
    ADD CONSTRAINT "FK_Payments_Invoices_InvoiceDataId" FOREIGN KEY ("InvoiceDataId") REFERENCES "Invoices"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "PendingInvoices"
    ADD CONSTRAINT "FK_PendingInvoices_Invoices_Id" FOREIGN KEY ("Id") REFERENCES "Invoices"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "RefundAddresses"
    ADD CONSTRAINT "FK_RefundAddresses_Invoices_InvoiceDataId" FOREIGN KEY ("InvoiceDataId") REFERENCES "Invoices"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "U2FDevices"
    ADD CONSTRAINT "FK_U2FDevices_AspNetUsers_ApplicationUserId" FOREIGN KEY ("ApplicationUserId") REFERENCES "AspNetUsers"("Id") ON DELETE RESTRICT;
ALTER TABLE ONLY "UserStore"
    ADD CONSTRAINT "FK_UserStore_AspNetUsers_ApplicationUserId" FOREIGN KEY ("ApplicationUserId") REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "UserStore"
    ADD CONSTRAINT "FK_UserStore_Stores_StoreDataId" FOREIGN KEY ("StoreDataId") REFERENCES "Stores"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "WalletTransactions"
    ADD CONSTRAINT "FK_WalletTransactions_Wallets_WalletDataId" FOREIGN KEY ("WalletDataId") REFERENCES "Wallets"("Id") ON DELETE CASCADE;
