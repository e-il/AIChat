# Frontend Verification

## Development

From the repository root:

```sh
npm --prefix frontend run dev -- --host 127.0.0.1 --port 5175
```

Open http://127.0.0.1:5175/. Vite proxies `/api` and `/chathub` to the backend on port 5000. Real use requires a healthy backend and a valid access code. No demo authentication bypass is included in the application.

## Build and Lint

```sh
npm --prefix frontend run build
npm --prefix frontend run lint
```

## Browser Regression

Keep the Vite development server running, then run:

```sh
npm --prefix frontend exec -- playwright install chromium
node frontend/test-ui.cjs
```

An installed Edge or Chrome can be used instead of downloading Chromium:

```sh
PLAYWRIGHT_CHANNEL=msedge node frontend/test-ui.cjs
PLAYWRIGHT_CHANNEL=chrome node frontend/test-ui.cjs
```

Set `UI_BASE_URL` to use a different development port. The script uses a fresh browser context and mocked HTTP/WebSocket responses. It never needs production credentials or a real model service. A nonzero exit code indicates failure.

Coverage includes login validation, suggestion drafts, IME composition, canceled uploads, preserving failed memory drafts, server-confirmed memory toggles with failure recovery and boundary payloads, model type preservation, prompt editing, history search, streaming conversation isolation, disconnect recovery, and responsive layouts at 1440/768/390/320 pixels. Screenshots are written to `frontend/artifacts/ui/` (ignored by Git).

Server-side cancellation, user isolation, and exclusion boundaries are tested separately in [MemoryModeTests.cs](../backend/AIChat.Api.Tests/MemoryModeTests.cs). Run `dotnet test backend/AIChat.Api.Tests/AIChat.Api.Tests.csproj` from the repository root. These tests use temporary files and do not call Azure.

This test does not verify backend authentication, authorization, Azure generation, actual file uploads, Docker deployment, Safari behavior, or mobile software keyboards. The older `test-screenshot.cjs` script depends on a real backend and is not the regression gate for this redesign.

## Visual Structure

Design tokens and shared layout styles live in `src/index.css`. The palette uses paper-white surfaces, a graphite sidebar, vermilion actions and secondary green/amber states. `Dropdown` and `Dialog` in `src/components/Common` are shared interaction primitives. Syntax-highlighting language definitions load on demand.