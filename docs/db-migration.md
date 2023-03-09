
# Migration from SQLite and MySQL to Postgres

## Introduction

This document is intended for BTCPay Server integrators such as Raspiblitz, Umbrel, Embassy OS or anybody running BTCPay Server on SQLite or MySql.

If you are a user of an integrated solution, please contact the integrator directly and provide them with the link to this document.

BTCPay Server has for long time supported three different backends:
1. Postgres
2. SQLite
3. MySql

While most of our users are using the Postgres backend, maintaining supports for all those databases has been very challenging, and Postgres is the only one part of our test suite.

As a result, we regret to inform you that we decided to stop the support of MySql and SQLite.

We understand that dropping support might be painful for users and integrators of our product, and we will do our best to provide a migration path.

Please keep us informed if you experience any issues while migrating on [our community chat](https://chat.btcpayserver.org).

## Procedure

In order to successfully migrate, you will need to run BTCPay Server `1.7.8 or newer`.

As a reminder there are three settings controlling the choice of backend of BTCPay Server which can be controller by command line, environment variable or configuration settings.

| Command line argument  | Environment variable |
|---|---|
| --postgres | BTCPAY_POSTGRES="..."  |
| --mysql  |  BTCPAY_MYSQL="..."  |
| --sqlitefile  | BTCPAY_SQLITEFILE="blah.db"  |

If you are currently using `mysql` or `sqlitefile`, and you wish to migrate to postgres, you simply need to add the command line argument `--postgres` or the environment variable `BTCPAY_POSTGRES` pointing to a fresh postgres database.

It is strongly advised not to create a database in Postgres before performing the migration with BTCPay Server. This is because BTCPay Server will automatically create the necessary database for you. However, if you must create the database manually, please ensure that the `C_TYPE` and `COLLATE` settings are both set to `C`.

**Careful: Do not remove the former mysql or sqlitefile setting, you should have both: the postgres setting and the former sqlite/mysql setting**

From `1.7.8`, BTCPay Server will interprete this and attempt to copy the data from mysql and sqlite into the new postgres database.

Note that once the migration is complete, the old `mysql` and `sqlite` settings will simply be ignored.

If the migration fails, you can revert the `postgres` setting you added, so the next restart will run on the old unsupported database. You can retry a migration by adding the `postgres` setting again.

## Known issues

* The migration script isn't very optimized, and will attempt to load every table in memory. If your `sqlite` or `mysql` database is too big, you may experience an Out Of Memory issue. If that happen to you, please contact us.
* There are no migration for plugin's data.
