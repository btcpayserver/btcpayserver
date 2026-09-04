---
name: playwright-test-patterns
description: Use when writing or refactoring Playwright tests in BTCPayServer. Covers PMO/Page Model Object usage, selector encapsulation, and avoiding over-engineering.
---

# Playwright Test Patterns

Use these patterns when writing or refactoring Playwright tests in BTCPayServer.

## Page Model Objects

- Creating Page Model Objects (PMOs) is encouraged when test code repeats UI interactions or assertions.
- PMOs should make tests more readable by exposing user-level actions and assertions.
- PMOs should hide repeated selector logic from test bodies.
- PMOs should expose methods such as `AssertSearchText(value)` or `SelectDateRangePreset(name)` instead of requiring repeated `Expect(...).ToHave...` calls at test call sites.
- PMOs should use stable selector hooks, preferably BEM class selectors for frontend components.

## Avoid Over-Engineering

- Do not create a PMO when the tested UI is very local to one test class and unlikely to be reused elsewhere.
- For page-specific controls used only in one test class, prefer small local helpers inside the test class.
- Keep PMOs focused on reusable components or page flows.
- Do not add abstraction layers that only wrap one obvious Playwright call unless it meaningfully improves readability or removes repetition.

## Test Through Real Interfaces

- Prefer using Playwright and/or `BTCPayServerClient` over reproducing application behavior by manually instantiating controllers.
- Use controller instantiation only when the test is specifically about controller internals and a browser/API flow would not exercise the behavior clearly.

## Assertions

- Prefer Playwright's built-in `Expect` API over manually fetching elements and asserting on their state.
- Playwright assertions automatically wait for the expected condition, making tests less verbose and less prone to flakiness.
- Prefer `await Expect(locator).ToHaveCountAsync(1)` over `Assert.Equal(1, await locator.CountAsync())`.
- Prefer `await Expect(locator).ToContainTextAsync(text)` over `Assert.Contains(text, await locator.TextContentAsync())`.
- Prefer `await Expect(locator).ToHaveValueAsync(value)` over `Assert.Equal(value, await locator.InputValueAsync())`.
- Prefer `await Expect(page).ToHaveURLAsync(...)` over direct assertions on `page.Url`.
- Do not call `WaitForLoadStateAsync` before a Playwright assertion; the `Expect` API waits for the expected state.
- Add `using static Microsoft.Playwright.Assertions;` when using `Expect`.

## Selector Guidance

- Prefer BEM class selectors for reusable UI hooks.
- Avoid direct ids in Playwright tests for reusable components when BEM hooks exist.
- Keep page-specific selectors near the page-specific test or PMO.
- When a selector is used in multiple tests, consider moving it behind a PMO action or assertion.

## Refactoring Existing Tests

- Prefer modifying or extending an existing relevant test over writing a new test.
- Add a new test only when no existing scenario naturally covers the behavior or when combining scenarios would make the test unclear.
- First identify repeated interaction/assertion sequences.
- Move repeated sequences into a PMO when they represent reusable component or page behavior.
- Keep one-off logic in the test if abstraction would obscure the scenario.
- Run the relevant test or test project build after refactoring Playwright selectors or PMOs.

## Running And Debugging Tests

- Before debugging BTCPay Server or running tests, start the test dependencies by running `docker-compose up -d dev` from the `BTCPayServer.Tests` directory.
