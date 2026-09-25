---
name: playwright-test-patterns
description: Use when writing, refactoring, running, or debugging Playwright tests in BTCPayServer. Covers local test setup, PMO/Page Model Object usage, selector encapsulation, and avoiding over-engineering.
---

# Playwright Test Patterns

Follow [Testing](../../../docs/maintainers/README.md#testing) and [Coding conventions](../../../docs/maintainers/README.md#frontend-selectors).

## Running and Debugging Tests

- Before debugging BTCPay Server or running tests, start the test dependencies by running `docker-compose up -d dev` from the `BTCPayServer.Tests` directory.
- Run tests directly on the host rather than through Docker Compose. From the repository root, run a specific test with:

```sh
dotnet test --project BTCPayServer.Tests/BTCPayServer.Tests.csproj --filter-method BTCPayServer.Tests.BitpayTests.CanUsePairing
```

- Replace the value passed to `--filter-method` with the fully qualified test method to run another test.
- Run the relevant test or test project build after changing Playwright selectors or Page Model Objects.
