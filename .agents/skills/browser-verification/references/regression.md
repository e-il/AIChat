# Regression Tests and Mocks

The existing [UI regression](../../../../frontend/test-ui.cjs) is the starting point for this project. Add focused assertions to its mock routes and interaction flow rather than duplicating authentication, data seeding, or SignalR setup.

## Determinism

- Seed a fresh browser context, never the developer's profile. Use unique IDs and strictly ordered timestamps where message order matters.
- Mock `/api` and SignalR negotiation plus WebSocket frames before loading the app. Assert the request payload as well as the displayed result.
- Do not simulate a slow upload with a fixed delay. Hold the mocked response on a promise, perform cancellation, then explicitly release it and assert that the attachment does not reappear.
- Register `page.waitForResponse` before clicking. A fulfilled request alone is not success: assert the committed UI state.
- Prefer a role and accessible name over generated CSS classes. If a label also matches a toolbar control, use `getByRole('textbox', { name: ... })` scoped to the dialog.

## Existing Harness Pattern

Use the repository's installed `playwright` and Node assertions. No `@playwright/test` dependency is required:

```js
const { chromium } = require('playwright');
const assert = require('node:assert/strict');

const browser = await chromium.launch({ channel: process.env.PLAYWRIGHT_CHANNEL || undefined });
try {
  const context = await browser.newContext();
  const page = await context.newPage();
  await page.route('**/api/example', route => route.fulfill({ json: { value: 'fixture' } }));
  await page.goto(process.env.UI_BASE_URL || 'http://127.0.0.1:5175');
  await page.getByRole('button', { name: 'Save', exact: true }).click();
  await page.getByRole('status').waitFor();
  assert.equal(await page.getByRole('textbox', { name: 'Name' }).inputValue(), 'fixture');
} finally {
  await browser.close();
}
```

Adapt the example to a real workflow and wrap it in the existing async entry point. Do not add dummy UI just to satisfy an example locator.

## Important Failure Checks

- A failed mutation preserves editable content and displays an error.
- Authorization failure does not silently become a successful empty response.
- IME composition does not submit the message.
- Canceling an async operation discards late results.
- Changing conversations does not display another conversation's stream or error.
- A disconnected stream releases its input lock.
- A privacy switch changes its displayed state only after server confirmation.

Assertions against mocks verify client behavior, not real server authorization or provider reliability. Use backend tests for server invariants and explicit integration tests for real services.