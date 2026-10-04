# Interactive Browser Exploration

Prefer existing agent browser tools when they provide navigation, accessible snapshots, actions, and screenshots. Tool names vary between hosts: use only tools actually available, not vendor-specific assumed names.

If `playwright-cli` is already installed, inspect its version/help before relying on optional flags. The CLI is an optional exploration route, not an application dependency.

```sh
command -v playwright-cli
playwright-cli --help
playwright-cli -s=aichat-check open http://127.0.0.1:5175
playwright-cli -s=aichat-check snapshot
```

Use references from the latest snapshot for `click`, `fill`, or `select`. References can change after navigation or DOM updates; do not reuse stale IDs blindly. Capture a new snapshot after a meaningful state change, not in a polling loop.

```sh
playwright-cli -s=aichat-check resize 390 844
playwright-cli -s=aichat-check screenshot
playwright-cli -s=aichat-check close
```

Use `run-code` only for a task-specific operation that normal locators cannot express. Prefer locator/response/event waits over `waitForTimeout` or a general `networkidle` requirement, especially with persistent SignalR connections.

## Sessions and Storage

- Use a named, isolated session. Never use global close/kill commands that could affect other work.
- Persistent profiles and connections to the user's existing browser require explicit authorization and a reason the isolated fixture cannot serve.
- Do not enumerate cookies or localStorage to discover credentials. Use documented synthetic fixture values.
- Storage-state exports can contain live secrets. Keep them out of version control and shared artifacts.
- Standard storage-state export does not imply sessionStorage is saved. IndexedDB inclusion is version- and option-dependent; verify the installed API rather than claiming all storage is captured.
- File uploads and downloads must use approved paths. Do not upload repository secrets, logs, or arbitrary files just because a page requests them.

After identifying the failure, return to the smallest owning component or service and convert the observation into a reproducible assertion.