using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260828051627_StoreInvitations")]
    public partial class StoreInvitations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                                  CREATE TABLE store_invitations (
                                      user_id text NOT NULL,
                                      store_id text NOT NULL,
                                      role_id text NULL,
                                      invited_by_user_id text NULL,
                                      created timestamp with time zone NOT NULL,
                                      expires_at timestamp with time zone NOT NULL,
                                      token_hash text NOT NULL,
                                      CONSTRAINT pk_store_invitations PRIMARY KEY (user_id, store_id),
                                      CONSTRAINT fk_store_invitations_user_id FOREIGN KEY (user_id) REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE,
                                      CONSTRAINT fk_store_invitations_store_id FOREIGN KEY (store_id) REFERENCES "Stores" ("Id") ON DELETE CASCADE,
                                      CONSTRAINT fk_store_invitations_role_id FOREIGN KEY (role_id) REFERENCES "StoreRoles" ("Id") ON DELETE CASCADE,
                                       CONSTRAINT uq_store_invitations_token_hash UNIQUE (token_hash)
                                  );
                                  CREATE INDEX ix_store_invitations_store_id ON store_invitations (store_id);
                                  CREATE INDEX ix_store_invitations_role_id ON store_invitations (role_id);
                                  """);
        }
    }
}
