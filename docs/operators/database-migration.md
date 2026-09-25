# Migrate SQLite or MySQL to PostgreSQL

Current BTCPay Server releases support PostgreSQL only. The automatic copier
for legacy SQLite and MySQL databases exists in BTCPay Server 1.7.8 through
1.13.7, but not in v2 or current `master`. Use v1.13.7 as a temporary migration
bridge before upgrading to a current release.

This procedure is for integrators and custom deployments. Users of packaged
node products should ask their integrator to perform and support the migration.
Official Docker deployments already use PostgreSQL; follow their
[backup and restore guide](https://github.com/btcpayserver/btcpayserver-docker/blob/master/docs/backup-restore.md)
instead of this procedure.

## Before Starting

1. Stop writes to the instance and take a tested backup of the legacy database,
   application data, wallet material, and plugin data.
2. Record the exact legacy connection setting and the BTCPay Server version.
3. Prepare enough memory for the migration. The bridge can load a complete
   table into memory.
4. Provision an empty PostgreSQL target. If creating it manually, use `C` for
   both `LC_CTYPE` and `LC_COLLATE`.
5. Obtain the
   [v1.13.7 release](https://github.com/btcpayserver/btcpayserver/releases/tag/v1.13.7).

Plugin-owned data is not copied by this legacy migrator. Inventory plugins and
follow each plugin's migration or export procedure before proceeding.

## Run the Bridge Release

Configure v1.13.7 with both the existing database and the empty PostgreSQL
target. Do not remove the legacy setting during the copy.

| Database | Legacy setting | PostgreSQL setting |
|---|---|---|
| SQLite | `sqlitefile`, `BTCPAY_SQLITEFILE`, or `--sqlitefile` | `postgres`, `BTCPAY_POSTGRES`, or `--postgres` |
| MySQL | `mysql`, `BTCPAY_MYSQL`, or `--mysql` | `postgres`, `BTCPAY_POSTGRES`, or `--postgres` |

Start v1.13.7 and monitor its logs. It should report the source type, each table
being migrated, and `Migration to postgres from ... successful`. The target
must be empty or a target previously created by this migration; the bridge
refuses an unrelated initialized database.

If migration fails, stop the process and preserve its logs. Remove the
PostgreSQL setting to run the bridge against the unchanged legacy database, or
point the next attempt at a new empty PostgreSQL target. Do not let both copies
serve production traffic.

## Verify and Upgrade

1. Start v1.13.7 using only PostgreSQL.
2. Verify accounts, stores, wallet settings, recent invoices, payments,
   webhooks, and any exported or separately migrated plugin data.
3. Create a PostgreSQL backup and retain the untouched legacy backup.
4. Upgrade using the procedure for your deployment, then inspect every startup
   migration and error before reopening traffic.

For migration failures, provide the bridge version, source database type,
redacted logs, and approximate database size in the
[community chat](https://chat.btcpayserver.org/). Never publish connection
strings or wallet secrets.
