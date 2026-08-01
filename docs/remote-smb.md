# Remote connections (SMB / Samba)

Duetto can browse and transfer files over SMB (SMB 2/3 — Samba, Windows shares,
NAS boxes) alongside the local filesystem, using a pure-managed client
([SMBLibrary](https://github.com/talaloni/smblibrary)) — no OS mount required.

## Adding a connection

Open the drive popover by clicking the volume chip in the path bar, choose
**Connect…** (⌘K / Ctrl K), then set **Protocol** to **SMB / Samba**. Fill in:

| Field | Notes |
|---|---|
| Name | Display name for the share (shown in the popover) |
| Host | Hostname or IP address of the server |
| Port | Always 445 (SMBLibrary connects over direct-TCP; a custom port is not supported) |
| Guest | Check to connect anonymously with no credentials |
| Username | SMB login user (hidden when Guest is checked) |
| Password | SMB password (hidden when Guest is checked) |
| Domain / Workgroup | Optional NTLM domain or workgroup |
| Initial path | Directory to open on connect. **Leave blank (or `/`) to land on the server's share list**; use `/share/sub` to open a specific folder |
| Save password | When checked, the password is stored obfuscated in `smb-connections.json` (see caveat below). When unchecked, you are prompted each time. Guest connections never store a secret. |

Click **Connect**. Unlike SSH, SMB has no host-key pinning step.

The **root of a connection is the server's list of shares** — `smb://<id>/` shows
each share as a folder; open one to browse its tree
(`smb://<id>/<share>/<path>`).

Saved connections appear in the **CONNECTED SHARES** section of the drive popover,
tagged **SMB** (SFTP connections are tagged **SFTP** in the same list). Click a
share to connect and navigate to it. A **Disconnect** row appears when a remote
pane is active.

## Where configuration lives

| Platform | Directory |
|---|---|
| macOS | `~/Library/Application Support/Duetto/` |
| Linux | `$XDG_CONFIG_HOME/duetto/` or `~/.config/duetto/` |
| Windows | `%APPDATA%\Duetto\` |

SMB profiles are stored separately from SFTP:

- `smb-connections.json` — saved SMB profiles (host, port, username, domain,
  guest flag, initial path, save-password flag, obfuscated password).

## Security caveat

Saved SMB passwords are **obfuscated** using a machine-derived key
(AES-256-CBC, SHA-256 of a machine identifier). This is reversible obfuscation,
**not** secure encrypted storage. Anyone with read access to
`smb-connections.json` and the same machine identity can recover the plaintext.
For sensitive credentials, leave **Save password** unchecked and enter the
password at connect time, or use a guest share.

## Remote operations

All standard file operations work over SMB: browse, copy/move (local↔remote and
remote↔remote), new folder/file, rename (`F2`), delete (`F8`/`Del` — permanent,
no trash on remote), and scoped recursive search (`Ctrl`/`Cmd`+`F`). Writes
finish atomically via a `.part` temp + server-side rename. Live directory
watching is not supported on remote panes; use `F5` or navigate away and back to
refresh. SMB does not expose POSIX permissions; entries show read-only vs
read-write access.

## Running the SMB integration tests

The unit tests use an in-memory fake, so `dotnet test` needs no server. To
exercise the real client end-to-end against throwaway containers:

```sh
scripts/smoke.sh
```

This brings up `docker-compose.yml` (Samba with an authenticated `duetto` share
and a guest `public` share, plus SFTP and MinIO backends), runs the
`Category=Integration` tests (`SmbIntegrationTests` + `SftpIntegrationTests`),
and tears the containers down. Requires Docker and a free host port 445.
