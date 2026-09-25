# Testing

## Test Environment

Start dependencies from `BTCPayServer.Tests` before running integration or Playwright tests:

```sh
docker-compose up -d dev
```

Run tests on the host. Run the full project from the repository root with:

```sh
dotnet test --project BTCPayServer.Tests/BTCPayServer.Tests.csproj
```

Run one test with its fully qualified method name:

```sh
dotnet test --project BTCPayServer.Tests/BTCPayServer.Tests.csproj --filter-method BTCPayServer.Tests.BitpayTests.CanUsePairing
```

If the dependency environment becomes stale, run `docker-compose down --volumes`, then `docker-compose pull` and `docker-compose up -d dev` from `BTCPayServer.Tests`.

## Test Design

- Prefer extending an existing relevant scenario over adding a separate test.
- Exercise real browser or `BTCPayServerClient` interfaces instead of manually constructing controllers, unless controller internals are the subject of the test.
- Use Playwright's auto-waiting `Expect` assertions; do not add `WaitForLoadStateAsync` before them.
- Prefer `Expect` assertions such as `ToHaveCountAsync`, `ToContainTextAsync`,
  `ToHaveValueAsync`, and `ToHaveURLAsync` over manually fetching state. Add
  `using static Microsoft.Playwright.Assertions;` where needed.
- Keep one-off selectors and helpers in the test. Introduce a Page Model Object only for repeated component or page behavior.
- Page Model Objects should expose user-level actions and assertions and hide selector details.
- Prefer stable BEM class hooks for reusable frontend components.

[`BTCPayServer.Tests/README.md`](../../BTCPayServer.Tests/README.md) documents payment simulation, Bitcoin and Lightning helper scripts, Polar, and the altcoin test environment.
