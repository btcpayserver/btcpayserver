-- Consolidated schema and seed data through 20231020135844_AddBoltcardsTable.
CREATE TABLE "AddressInvoices" (
    "Address" text NOT NULL,
    "InvoiceDataId" text,
    "CreatedTime" timestamp with time zone
);
CREATE TABLE "ApiKeys" (
    "Id" character varying(50) NOT NULL,
    "StoreId" character varying(50),
    "Type" integer DEFAULT 0 NOT NULL,
    "UserId" character varying(50),
    "Label" text,
    "Blob" bytea,
    "Blob2" jsonb
);
CREATE TABLE "Apps" (
    "Id" text NOT NULL,
    "AppType" text,
    "Created" timestamp with time zone NOT NULL,
    "Name" text,
    "Settings" text,
    "StoreDataId" text,
    "TagAllInvoices" boolean DEFAULT false NOT NULL,
    "Archived" boolean DEFAULT false NOT NULL
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
    "RequiresEmailConfirmation" boolean DEFAULT false NOT NULL,
    "Created" timestamp with time zone,
    "DisabledNotifications" text,
    "Blob" bytea,
    "Blob2" jsonb
);
CREATE TABLE "CustodianAccount" (
    "Id" character varying(50) NOT NULL,
    "StoreId" character varying(50) NOT NULL,
    "CustodianCode" character varying(50) NOT NULL,
    "Name" character varying(50),
    "Blob" bytea,
    "Blob2" jsonb
);
CREATE TABLE "Fido2Credentials" (
    "Id" text NOT NULL,
    "Name" text,
    "ApplicationUserId" text,
    "Blob" bytea,
    "Type" integer NOT NULL,
    "Blob2" jsonb
);
CREATE TABLE "Files" (
    "Id" text NOT NULL,
    "FileName" text,
    "StorageFileName" text,
    "Timestamp" timestamp with time zone NOT NULL,
    "ApplicationUserId" text
);
CREATE TABLE "Forms" (
    "Id" text NOT NULL,
    "Name" text,
    "StoreId" text,
    "Config" jsonb,
    "Public" boolean NOT NULL
);
CREATE TABLE "InvoiceEvents" (
    "InvoiceDataId" text NOT NULL,
    "UniqueId" text NOT NULL,
    "Message" text,
    "Timestamp" timestamp with time zone NOT NULL,
    "Severity" integer DEFAULT 0 NOT NULL
);
CREATE TABLE "InvoiceSearches" (
    "Id" integer NOT NULL,
    "InvoiceDataId" character varying(255),
    "Value" text
);
CREATE SEQUENCE "InvoiceSearches_Id_seq"
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;
ALTER SEQUENCE "InvoiceSearches_Id_seq" OWNED BY "InvoiceSearches"."Id";
CREATE TABLE "InvoiceWebhookDeliveries" (
    "InvoiceId" text NOT NULL,
    "DeliveryId" text NOT NULL
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
    "StoreDataId" text,
    "Archived" boolean DEFAULT false NOT NULL,
    "CurrentRefundId" text,
    "Blob2" jsonb
);
CREATE TABLE "LightningAddresses" (
    "Username" text NOT NULL,
    "StoreDataId" text NOT NULL,
    "Blob" bytea,
    "Blob2" jsonb
);
CREATE TABLE "Notifications" (
    "Id" character varying(36) NOT NULL,
    "Created" timestamp with time zone NOT NULL,
    "ApplicationUserId" character varying(50) NOT NULL,
    "NotificationType" character varying(100) NOT NULL,
    "Seen" boolean NOT NULL,
    "Blob" bytea,
    "Blob2" jsonb
);
CREATE TABLE "OffchainTransactions" (
    "Id" character varying(64) NOT NULL,
    "Blob" bytea
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
CREATE TABLE "PayjoinLocks" (
    "Id" character varying(100) NOT NULL
);
CREATE TABLE "PaymentRequests" (
    "Id" text NOT NULL,
    "StoreDataId" text,
    "Status" integer NOT NULL,
    "Blob" bytea,
    "Created" timestamp with time zone DEFAULT '1970-01-01 00:00:00+00'::timestamp with time zone NOT NULL,
    "Archived" boolean DEFAULT false NOT NULL,
    "Blob2" jsonb
);
CREATE TABLE "Payments" (
    "Id" text NOT NULL,
    "Blob" bytea,
    "InvoiceDataId" text,
    "Accounted" boolean DEFAULT false NOT NULL,
    "Blob2" jsonb,
    "Type" text
);
CREATE TABLE "PayoutProcessors" (
    "Id" text NOT NULL,
    "StoreId" text,
    "PaymentMethod" text,
    "Processor" text,
    "Blob" bytea,
    "Blob2" jsonb
);
CREATE TABLE "Payouts" (
    "Id" character varying(30) NOT NULL,
    "Date" timestamp with time zone NOT NULL,
    "PullPaymentDataId" character varying(30),
    "State" character varying(20) NOT NULL,
    "PaymentMethodId" character varying(20) NOT NULL,
    "Destination" text,
    "Blob" bytea,
    "Proof" bytea,
    "StoreDataId" text
);
CREATE TABLE "PendingInvoices" (
    "Id" text NOT NULL
);
CREATE TABLE "PlannedTransactions" (
    "Id" character varying(100) NOT NULL,
    "BroadcastAt" timestamp with time zone NOT NULL,
    "Blob" bytea
);
CREATE TABLE "PullPayments" (
    "Id" character varying(30) NOT NULL,
    "StoreId" character varying(50),
    "Period" bigint,
    "StartDate" timestamp with time zone NOT NULL,
    "EndDate" timestamp with time zone,
    "Archived" boolean NOT NULL,
    "Blob" bytea
);
CREATE TABLE "Refunds" (
    "InvoiceDataId" text NOT NULL,
    "PullPaymentDataId" text NOT NULL
);
CREATE TABLE "Settings" (
    "Id" text NOT NULL,
    "Value" jsonb
);
CREATE TABLE "StoreRoles" (
    "Id" text NOT NULL,
    "StoreDataId" text,
    "Role" text NOT NULL,
    "Permissions" text[] NOT NULL
);
CREATE TABLE "StoreSettings" (
    "Name" text NOT NULL,
    "StoreId" text NOT NULL,
    "Value" jsonb
);
CREATE TABLE "StoreWebhooks" (
    "StoreId" character varying(50) NOT NULL,
    "WebhookId" character varying(25) NOT NULL
);
CREATE TABLE "Stores" (
    "Id" text NOT NULL,
    "DerivationStrategy" text,
    "SpeedPolicy" integer NOT NULL,
    "StoreCertificate" bytea,
    "StoreName" text,
    "StoreWebsite" text,
    "StoreBlob" jsonb,
    "DerivationStrategies" jsonb,
    "DefaultCrypto" text,
    "Archived" boolean DEFAULT false NOT NULL
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
CREATE TABLE "WalletObjectLinks" (
    "WalletId" text NOT NULL,
    "AType" text NOT NULL,
    "AId" text NOT NULL,
    "BType" text NOT NULL,
    "BId" text NOT NULL,
    "Data" jsonb
);
CREATE TABLE "WalletObjects" (
    "WalletId" text NOT NULL,
    "Type" text NOT NULL,
    "Id" text NOT NULL,
    "Data" jsonb
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
CREATE TABLE "WebhookDeliveries" (
    "Id" text NOT NULL,
    "WebhookId" text NOT NULL,
    "Timestamp" timestamp with time zone NOT NULL,
    "Pruned" boolean NOT NULL,
    "Blob" jsonb NOT NULL
);
CREATE TABLE "Webhooks" (
    "Id" character varying(25) NOT NULL,
    "Blob" bytea NOT NULL,
    "Blob2" jsonb
);
CREATE TABLE boltcards (
    id character varying(32) NOT NULL,
    counter integer DEFAULT 0 NOT NULL,
    ppid character varying(30),
    version integer DEFAULT 0 NOT NULL
);
ALTER TABLE ONLY "InvoiceSearches" ALTER COLUMN "Id" SET DEFAULT nextval('"InvoiceSearches_Id_seq"'::regclass);
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
ALTER TABLE ONLY "CustodianAccount"
    ADD CONSTRAINT "PK_CustodianAccount" PRIMARY KEY ("Id");
ALTER TABLE ONLY "Fido2Credentials"
    ADD CONSTRAINT "PK_Fido2Credentials" PRIMARY KEY ("Id");
ALTER TABLE ONLY "Files"
    ADD CONSTRAINT "PK_Files" PRIMARY KEY ("Id");
ALTER TABLE ONLY "Forms"
    ADD CONSTRAINT "PK_Forms" PRIMARY KEY ("Id");
ALTER TABLE ONLY "InvoiceEvents"
    ADD CONSTRAINT "PK_InvoiceEvents" PRIMARY KEY ("InvoiceDataId", "UniqueId");
ALTER TABLE ONLY "InvoiceSearches"
    ADD CONSTRAINT "PK_InvoiceSearches" PRIMARY KEY ("Id");
ALTER TABLE ONLY "InvoiceWebhookDeliveries"
    ADD CONSTRAINT "PK_InvoiceWebhookDeliveries" PRIMARY KEY ("InvoiceId", "DeliveryId");
ALTER TABLE ONLY "Invoices"
    ADD CONSTRAINT "PK_Invoices" PRIMARY KEY ("Id");
ALTER TABLE ONLY "LightningAddresses"
    ADD CONSTRAINT "PK_LightningAddresses" PRIMARY KEY ("Username");
ALTER TABLE ONLY "Notifications"
    ADD CONSTRAINT "PK_Notifications" PRIMARY KEY ("Id");
ALTER TABLE ONLY "OffchainTransactions"
    ADD CONSTRAINT "PK_OffchainTransactions" PRIMARY KEY ("Id");
ALTER TABLE ONLY "PairedSINData"
    ADD CONSTRAINT "PK_PairedSINData" PRIMARY KEY ("Id");
ALTER TABLE ONLY "PairingCodes"
    ADD CONSTRAINT "PK_PairingCodes" PRIMARY KEY ("Id");
ALTER TABLE ONLY "PayjoinLocks"
    ADD CONSTRAINT "PK_PayjoinLocks" PRIMARY KEY ("Id");
ALTER TABLE ONLY "PaymentRequests"
    ADD CONSTRAINT "PK_PaymentRequests" PRIMARY KEY ("Id");
ALTER TABLE ONLY "Payments"
    ADD CONSTRAINT "PK_Payments" PRIMARY KEY ("Id");
ALTER TABLE ONLY "PayoutProcessors"
    ADD CONSTRAINT "PK_PayoutProcessors" PRIMARY KEY ("Id");
ALTER TABLE ONLY "Payouts"
    ADD CONSTRAINT "PK_Payouts" PRIMARY KEY ("Id");
ALTER TABLE ONLY "PendingInvoices"
    ADD CONSTRAINT "PK_PendingInvoices" PRIMARY KEY ("Id");
ALTER TABLE ONLY "PlannedTransactions"
    ADD CONSTRAINT "PK_PlannedTransactions" PRIMARY KEY ("Id");
ALTER TABLE ONLY "PullPayments"
    ADD CONSTRAINT "PK_PullPayments" PRIMARY KEY ("Id");
ALTER TABLE ONLY "Refunds"
    ADD CONSTRAINT "PK_Refunds" PRIMARY KEY ("InvoiceDataId", "PullPaymentDataId");
ALTER TABLE ONLY "Settings"
    ADD CONSTRAINT "PK_Settings" PRIMARY KEY ("Id");
ALTER TABLE ONLY "StoreRoles"
    ADD CONSTRAINT "PK_StoreRoles" PRIMARY KEY ("Id");
ALTER TABLE ONLY "StoreSettings"
    ADD CONSTRAINT "PK_StoreSettings" PRIMARY KEY ("StoreId", "Name");
ALTER TABLE ONLY "StoreWebhooks"
    ADD CONSTRAINT "PK_StoreWebhooks" PRIMARY KEY ("StoreId", "WebhookId");
ALTER TABLE ONLY "Stores"
    ADD CONSTRAINT "PK_Stores" PRIMARY KEY ("Id");
ALTER TABLE ONLY "U2FDevices"
    ADD CONSTRAINT "PK_U2FDevices" PRIMARY KEY ("Id");
ALTER TABLE ONLY "UserStore"
    ADD CONSTRAINT "PK_UserStore" PRIMARY KEY ("ApplicationUserId", "StoreDataId");
ALTER TABLE ONLY "WalletObjectLinks"
    ADD CONSTRAINT "PK_WalletObjectLinks" PRIMARY KEY ("WalletId", "AType", "AId", "BType", "BId");
ALTER TABLE ONLY "WalletObjects"
    ADD CONSTRAINT "PK_WalletObjects" PRIMARY KEY ("WalletId", "Type", "Id");
ALTER TABLE ONLY "WalletTransactions"
    ADD CONSTRAINT "PK_WalletTransactions" PRIMARY KEY ("WalletDataId", "TransactionId");
ALTER TABLE ONLY "Wallets"
    ADD CONSTRAINT "PK_Wallets" PRIMARY KEY ("Id");
ALTER TABLE ONLY "WebhookDeliveries"
    ADD CONSTRAINT "PK_WebhookDeliveries" PRIMARY KEY ("Id");
ALTER TABLE ONLY "Webhooks"
    ADD CONSTRAINT "PK_Webhooks" PRIMARY KEY ("Id");
ALTER TABLE ONLY boltcards
    ADD CONSTRAINT "PK_id" PRIMARY KEY (id);
CREATE INDEX "EmailIndex" ON "AspNetUsers" USING btree ("NormalizedEmail");
CREATE INDEX "IX_AddressInvoices_InvoiceDataId" ON "AddressInvoices" USING btree ("InvoiceDataId");
CREATE INDEX "IX_ApiKeys_StoreId" ON "ApiKeys" USING btree ("StoreId");
CREATE INDEX "IX_ApiKeys_UserId" ON "ApiKeys" USING btree ("UserId");
CREATE INDEX "IX_Apps_StoreDataId" ON "Apps" USING btree ("StoreDataId");
CREATE INDEX "IX_AspNetRoleClaims_RoleId" ON "AspNetRoleClaims" USING btree ("RoleId");
CREATE INDEX "IX_AspNetUserClaims_UserId" ON "AspNetUserClaims" USING btree ("UserId");
CREATE INDEX "IX_AspNetUserLogins_UserId" ON "AspNetUserLogins" USING btree ("UserId");
CREATE INDEX "IX_AspNetUserRoles_RoleId" ON "AspNetUserRoles" USING btree ("RoleId");
CREATE INDEX "IX_CustodianAccount_StoreId" ON "CustodianAccount" USING btree ("StoreId");
CREATE INDEX "IX_Fido2Credentials_ApplicationUserId" ON "Fido2Credentials" USING btree ("ApplicationUserId");
CREATE INDEX "IX_Files_ApplicationUserId" ON "Files" USING btree ("ApplicationUserId");
CREATE INDEX "IX_Forms_StoreId" ON "Forms" USING btree ("StoreId");
CREATE INDEX "IX_InvoiceSearches_InvoiceDataId" ON "InvoiceSearches" USING btree ("InvoiceDataId");
CREATE INDEX "IX_InvoiceSearches_Value" ON "InvoiceSearches" USING btree ("Value");
CREATE INDEX "IX_Invoices_Created" ON "Invoices" USING btree ("Created");
CREATE INDEX "IX_Invoices_Id_CurrentRefundId" ON "Invoices" USING btree ("Id", "CurrentRefundId");
CREATE INDEX "IX_Invoices_OrderId" ON "Invoices" USING btree ("OrderId");
CREATE INDEX "IX_Invoices_StoreDataId" ON "Invoices" USING btree ("StoreDataId");
CREATE INDEX "IX_LightningAddresses_StoreDataId" ON "LightningAddresses" USING btree ("StoreDataId");
CREATE INDEX "IX_Notifications_ApplicationUserId" ON "Notifications" USING btree ("ApplicationUserId");
CREATE INDEX "IX_PairedSINData_SIN" ON "PairedSINData" USING btree ("SIN");
CREATE INDEX "IX_PairedSINData_StoreDataId" ON "PairedSINData" USING btree ("StoreDataId");
CREATE INDEX "IX_PaymentRequests_Status" ON "PaymentRequests" USING btree ("Status");
CREATE INDEX "IX_PaymentRequests_StoreDataId" ON "PaymentRequests" USING btree ("StoreDataId");
CREATE INDEX "IX_Payments_InvoiceDataId" ON "Payments" USING btree ("InvoiceDataId");
CREATE INDEX "IX_PayoutProcessors_StoreId" ON "PayoutProcessors" USING btree ("StoreId");
CREATE INDEX "IX_Payouts_Destination_State" ON "Payouts" USING btree ("Destination", "State");
CREATE INDEX "IX_Payouts_PullPaymentDataId" ON "Payouts" USING btree ("PullPaymentDataId");
CREATE INDEX "IX_Payouts_State" ON "Payouts" USING btree ("State");
CREATE INDEX "IX_Payouts_StoreDataId" ON "Payouts" USING btree ("StoreDataId");
CREATE INDEX "IX_PullPayments_StoreId" ON "PullPayments" USING btree ("StoreId");
CREATE INDEX "IX_Refunds_PullPaymentDataId" ON "Refunds" USING btree ("PullPaymentDataId");
CREATE UNIQUE INDEX "IX_StoreRoles_StoreDataId_Role" ON "StoreRoles" USING btree ("StoreDataId", "Role");
CREATE INDEX "IX_U2FDevices_ApplicationUserId" ON "U2FDevices" USING btree ("ApplicationUserId");
CREATE INDEX "IX_UserStore_StoreDataId" ON "UserStore" USING btree ("StoreDataId");
CREATE INDEX "IX_WalletObjectLinks_WalletId_BType_BId" ON "WalletObjectLinks" USING btree ("WalletId", "BType", "BId");
CREATE INDEX "IX_WalletObjects_Type_Id" ON "WalletObjects" USING btree ("Type", "Id");
CREATE INDEX "IX_WebhookDeliveries_Timestamp" ON "WebhookDeliveries" USING btree ("Timestamp") WHERE ("Pruned" IS FALSE);
CREATE INDEX "IX_WebhookDeliveries_WebhookId" ON "WebhookDeliveries" USING btree ("WebhookId");
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
ALTER TABLE ONLY "CustodianAccount"
    ADD CONSTRAINT "FK_CustodianAccount_Stores_StoreId" FOREIGN KEY ("StoreId") REFERENCES "Stores"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "Fido2Credentials"
    ADD CONSTRAINT "FK_Fido2Credentials_AspNetUsers_ApplicationUserId" FOREIGN KEY ("ApplicationUserId") REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "Files"
    ADD CONSTRAINT "FK_Files_AspNetUsers_ApplicationUserId" FOREIGN KEY ("ApplicationUserId") REFERENCES "AspNetUsers"("Id") ON DELETE RESTRICT;
ALTER TABLE ONLY "Forms"
    ADD CONSTRAINT "FK_Forms_Stores_StoreId" FOREIGN KEY ("StoreId") REFERENCES "Stores"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "InvoiceEvents"
    ADD CONSTRAINT "FK_InvoiceEvents_Invoices_InvoiceDataId" FOREIGN KEY ("InvoiceDataId") REFERENCES "Invoices"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "InvoiceSearches"
    ADD CONSTRAINT "FK_InvoiceSearches_Invoices_InvoiceDataId" FOREIGN KEY ("InvoiceDataId") REFERENCES "Invoices"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "InvoiceWebhookDeliveries"
    ADD CONSTRAINT "FK_InvoiceWebhookDeliveries_Invoices_InvoiceId" FOREIGN KEY ("InvoiceId") REFERENCES "Invoices"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "InvoiceWebhookDeliveries"
    ADD CONSTRAINT "FK_InvoiceWebhookDeliveries_WebhookDeliveries_DeliveryId" FOREIGN KEY ("DeliveryId") REFERENCES "WebhookDeliveries"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "Invoices"
    ADD CONSTRAINT "FK_Invoices_Refunds_Id_CurrentRefundId" FOREIGN KEY ("Id", "CurrentRefundId") REFERENCES "Refunds"("InvoiceDataId", "PullPaymentDataId") ON DELETE RESTRICT;
ALTER TABLE ONLY "Invoices"
    ADD CONSTRAINT "FK_Invoices_Stores_StoreDataId" FOREIGN KEY ("StoreDataId") REFERENCES "Stores"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "LightningAddresses"
    ADD CONSTRAINT "FK_LightningAddresses_Stores_StoreDataId" FOREIGN KEY ("StoreDataId") REFERENCES "Stores"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "Notifications"
    ADD CONSTRAINT "FK_Notifications_AspNetUsers_ApplicationUserId" FOREIGN KEY ("ApplicationUserId") REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "PairedSINData"
    ADD CONSTRAINT "FK_PairedSINData_Stores_StoreDataId" FOREIGN KEY ("StoreDataId") REFERENCES "Stores"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "PaymentRequests"
    ADD CONSTRAINT "FK_PaymentRequests_Stores_StoreDataId" FOREIGN KEY ("StoreDataId") REFERENCES "Stores"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "Payments"
    ADD CONSTRAINT "FK_Payments_Invoices_InvoiceDataId" FOREIGN KEY ("InvoiceDataId") REFERENCES "Invoices"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "PayoutProcessors"
    ADD CONSTRAINT "FK_PayoutProcessors_Stores_StoreId" FOREIGN KEY ("StoreId") REFERENCES "Stores"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "Payouts"
    ADD CONSTRAINT "FK_Payouts_PullPayments_PullPaymentDataId" FOREIGN KEY ("PullPaymentDataId") REFERENCES "PullPayments"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "Payouts"
    ADD CONSTRAINT "FK_Payouts_Stores_StoreDataId" FOREIGN KEY ("StoreDataId") REFERENCES "Stores"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "PendingInvoices"
    ADD CONSTRAINT "FK_PendingInvoices_Invoices_Id" FOREIGN KEY ("Id") REFERENCES "Invoices"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "PullPayments"
    ADD CONSTRAINT "FK_PullPayments_Stores_StoreId" FOREIGN KEY ("StoreId") REFERENCES "Stores"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "Refunds"
    ADD CONSTRAINT "FK_Refunds_Invoices_InvoiceDataId" FOREIGN KEY ("InvoiceDataId") REFERENCES "Invoices"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "Refunds"
    ADD CONSTRAINT "FK_Refunds_PullPayments_PullPaymentDataId" FOREIGN KEY ("PullPaymentDataId") REFERENCES "PullPayments"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "StoreRoles"
    ADD CONSTRAINT "FK_StoreRoles_Stores_StoreDataId" FOREIGN KEY ("StoreDataId") REFERENCES "Stores"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "StoreSettings"
    ADD CONSTRAINT "FK_StoreSettings_Stores_StoreId" FOREIGN KEY ("StoreId") REFERENCES "Stores"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "StoreWebhooks"
    ADD CONSTRAINT "FK_StoreWebhooks_Stores_StoreId" FOREIGN KEY ("StoreId") REFERENCES "Stores"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "StoreWebhooks"
    ADD CONSTRAINT "FK_StoreWebhooks_Webhooks_WebhookId" FOREIGN KEY ("WebhookId") REFERENCES "Webhooks"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "U2FDevices"
    ADD CONSTRAINT "FK_U2FDevices_AspNetUsers_ApplicationUserId" FOREIGN KEY ("ApplicationUserId") REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "UserStore"
    ADD CONSTRAINT "FK_UserStore_AspNetUsers_ApplicationUserId" FOREIGN KEY ("ApplicationUserId") REFERENCES "AspNetUsers"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "UserStore"
    ADD CONSTRAINT "FK_UserStore_StoreRoles_Role" FOREIGN KEY ("Role") REFERENCES "StoreRoles"("Id");
ALTER TABLE ONLY "UserStore"
    ADD CONSTRAINT "FK_UserStore_Stores_StoreDataId" FOREIGN KEY ("StoreDataId") REFERENCES "Stores"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "WalletObjectLinks"
    ADD CONSTRAINT "FK_WalletObjectLinks_WalletObjects_WalletId_AType_AId" FOREIGN KEY ("WalletId", "AType", "AId") REFERENCES "WalletObjects"("WalletId", "Type", "Id") ON DELETE CASCADE;
ALTER TABLE ONLY "WalletObjectLinks"
    ADD CONSTRAINT "FK_WalletObjectLinks_WalletObjects_WalletId_BType_BId" FOREIGN KEY ("WalletId", "BType", "BId") REFERENCES "WalletObjects"("WalletId", "Type", "Id") ON DELETE CASCADE;
ALTER TABLE ONLY "WalletTransactions"
    ADD CONSTRAINT "FK_WalletTransactions_Wallets_WalletDataId" FOREIGN KEY ("WalletDataId") REFERENCES "Wallets"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY "WebhookDeliveries"
    ADD CONSTRAINT "FK_WebhookDeliveries_Webhooks_WebhookId" FOREIGN KEY ("WebhookId") REFERENCES "Webhooks"("Id") ON DELETE CASCADE;
ALTER TABLE ONLY boltcards
    ADD CONSTRAINT "FK_boltcards_PullPayments" FOREIGN KEY (ppid) REFERENCES "PullPayments"("Id") ON DELETE SET NULL;
INSERT INTO "StoreRoles" ("Id", "StoreDataId", "Role", "Permissions") VALUES ('Owner', NULL, 'Owner', '{btcpay.store.canmodifystoresettings,btcpay.store.cantradecustodianaccount,btcpay.store.canwithdrawfromcustodianaccount,btcpay.store.candeposittocustodianaccount}');
INSERT INTO "StoreRoles" ("Id", "StoreDataId", "Role", "Permissions") VALUES ('Guest', NULL, 'Guest', '{btcpay.store.canviewstoresettings,btcpay.store.canmodifyinvoices,btcpay.store.canviewcustodianaccounts,btcpay.store.candeposittocustodianaccount}');
