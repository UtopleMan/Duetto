# Duetto

An orthodox two-pane file manager for Windows, macOS, and Linux built with
Avalonia and .NET 10. Supports local filesystem browsing and SFTP remote
connections.

## Building

Requires .NET 10 SDK.

```
dotnet build Duetto.slnx
dotnet test Duetto.slnx
```

## Running

```
dotnet run --project src/Duetto
```

Override the OS chrome for preview:

```
dotnet run --project src/Duetto -- --chrome win|mac|gnome
```

## Packaging

Self-contained single-file binaries for all three platforms:

```
./scripts/publish-all.sh
```

Output goes to `dist/osx-arm64/Duetto`, `dist/win-x64/Duetto.exe`,
`dist/linux-x64/Duetto`, plus `dist/duetto-<rid>.zip` (~100 MB each).

macOS app bundle:

```
./scripts/make-app-bundle.sh
```

Output: `dist/Duetto.app`.

## Remote connections (SFTP)

### Adding a connection

Open the drive popover by clicking the volume chip in the path bar, then choose
**Connect…**. Fill in:

| Field | Notes |
|---|---|
| Name | Display name for the share (shown in the popover and GNOME Places rail) |
| Protocol | SFTP (only option in v1) |
| Host | Hostname or IP address |
| Port | Default 22 |
| Username | SSH login user |
| Auth | **Password** — enter a password; or **Key file** — path to a private key file (PEM/OpenSSH), optional passphrase |
| Initial path | Remote directory to open on connect (leave blank for home) |
| Save password | When checked, the secret is stored obfuscated in `connections.json` (see caveat below). When unchecked, you are prompted each time. |

Click **Test / Connect**. On first connect you will be asked to trust the server's
host key (trust-on-first-use). Once accepted, the fingerprint is pinned and any
future change triggers a warning.

Saved connections appear in the **CONNECTED SHARES** section of the drive popover
and in the GNOME Places rail under **Remote**. Click a share to connect and
navigate to it. A **Disconnect** row appears when a remote pane is active.

### Where configuration lives

| Platform | Directory |
|---|---|
| macOS | `~/Library/Application Support/Duetto/` |
| Linux | `$XDG_CONFIG_HOME/duetto/` or `~/.config/duetto/` |
| Windows | `%APPDATA%\Duetto\` |

Two files are written there:

- `connections.json` — saved connection profiles (host, port, username, auth
  mode, key path, initial path, save-password flag).
- `hostkeys.json` — pinned server fingerprints (trust-on-first-use store).

### Security caveat

Saved passwords and key passphrases are **obfuscated** using a machine-derived
key (AES-256-CBC, SHA-256 of a machine identifier). This is reversible
obfuscation, **not** secure encrypted storage. Anyone with read access to
`connections.json` and the same machine identity can recover the plaintext.
For servers where the password is sensitive, leave **Save password** unchecked
and enter the secret at connect time.

### Remote operations

All standard file operations work over SFTP: browse, copy/move (local↔remote and
remote↔remote via the two-tone progress strip), new folder/file, rename (F2),
delete (F8/Del — permanent, no Trash on remote), and scoped recursive search
(Ctrl/Cmd+F). Live directory watching is not supported on remote panes; use F5 or
navigate away and back to refresh.
