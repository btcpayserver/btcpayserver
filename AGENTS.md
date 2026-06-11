# Agent Instructions

## Creating Migrations

* Run `dotnet ef migrations add <migration-name>` to generate the migration.
* Copy the attributes from the generated `.Designer.cs` file to the `.cs` migration file.
* Remove the generated `.Designer.cs` file.
* Remove the `Down()` method.
* Do not use `migrationBuilder.IsNpgsql()`; assume PostgreSQL is used.
* If a migration cannot be generated through `dotnet ef migrations`, add a migration file prefixed by date in the `Migrations` folder, for example `20260525115757_passkey.cs`, and use `migrationBuilder.Sql` to run raw SQL.

## Updating `Changelog.md`

When asked to update or review the changelog, focus on user-visible changes and keep entries concise.

### Release Range

* Compare against the previous release tag, for example `v2.3.9..master` when preparing `2.4.0`.
* If the changelog branch contains changelog-only commits on top of `master`, compare against `master` to avoid including those commits in the review.
* Check whether the previous release tag is on the same ancestry path. If not, identify the practical post-release bump commit and compare from there as needed.

### What To Include

* Include features, fixes, improvements, regressions, deprecations, removals, and security-relevant behavior changes that users, admins, plugin authors, API users, or integrators may care about.
* Include UI fixes when they affect real usage, even if the code change is small.
* Include permission, authentication, wallet, checkout, Point of Sale, subscription, rate provider, plugin compatibility, and API behavior changes when they affect users or integrators.
* Include removals and deprecations under `Miscellaneous` unless they fit better under another existing section.

### What To Skip

* Skip purely internal refactors, file moves, test-only changes, warning fixes, dependency bumps for tests, and CI/tooling changes unless they affect users or release operators.
* Skip very technical route/controller/view-model reshuffling unless it changes public behavior or public API usage.
* Skip duplicate commits already covered by a previous patch release section.

### Style

* Use short bullet points under sections such as `New features`, `Fixes`, `Improvements`, and `Miscellaneous`.
* Prefer imperative phrasing: `Add`, `Fix`, `Allow`, `Improve`, `Remove`, `Deprecate`.
* Keep capitalization consistent with existing entries.
* Use product terminology consistently, for example `Point of Sale`, `Pull Payments`, `Pull Requests`, `Invoices`, `Apps`, `Keypad Point of Sale`, and `Greenfield API`.
* Wrap code identifiers and permissions in backticks, for example `` `CanSendStoreEmail` ``.
* Include PR or issue numbers when available, for example `(#7379)` or `(#7383 #7386)`.
* Include the contributor handle at the end when known, for example `@NicolasDorier`.

### Verification

* Review the final diff with `git diff -- Changelog.md`.
* Run `git diff --check -- Changelog.md` to catch whitespace issues.
* Verify authorship for added entries with `git show --no-patch --format='%h %an <%ae> %s' <commit>` when attribution is not obvious.
