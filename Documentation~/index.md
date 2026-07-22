# Lore VCS for Unity — Documentation

## Overview

`com.projectc.lore-unity` provides a full Lore VCS experience inside Unity Editor:

- **Server lifecycle management** — install, start, stop, health check
- **Repository operations** — status, stage, commit, push, sync
- **Branch management** — list, create, switch, merge
- **History browsing** — commit log with diff viewer
- **Settings** — configurable paths, auto-start, refresh interval

## Architecture

The package is Editor-only. All scripts live under `Editor/` and are not included in builds.

```
LoreCliService  ←→  lore.exe (Process)
       ↓
LoreCliParser   ←  stdout parsing
       ↓
LoreWindow      ←  UI Toolkit (UXML + USS)
       ↓
LoreSettings    ←  EditorPrefs
```

## Lore CLI Requirements

- `lore.exe` must be available (auto-installed or in PATH)
- `loreserver.exe` for remote operations (auto-installed or in PATH)
- Lore repository initialized in the project directory

## Configuration

Access via `Edit → Preferences → Lore`:

| Setting | Default | Description |
|---|---|---|
| Lore Executable Path | auto | Path to `lore.exe` |
| Server Executable Path | auto | Path to `loreserver.exe` |
| Server URL | `http://127.0.0.1:41339` | Lore server URL |
| Auto-start Server | true | Start server on Unity load |
| Auto-refresh Interval | 30s | Status refresh interval |

## Troubleshooting

### "lore.exe not found"
Run `Lore → Install Lore Server` or specify path in Preferences.

### "Server offline"
Check that `loreserver` is running. Use `Lore → Lore Window` server indicator to verify.

### "Parse error"
The CLI output format may have changed. Check Console for raw output. File an issue.
