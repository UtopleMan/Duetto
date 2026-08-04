# Theme (light / dark)

Duetto supports a **Light**, **Dark**, and **System** appearance. The dark palette
comes from the Claude design. The choice is a setting in the config file and applies
on the **next launch** (Duetto reads it once at startup).

## Setting it

Edit `settings.json` in Duetto's config directory and set the `theme` key:

```json
{
  "theme": "Dark"
}
```

Allowed values (case-insensitive): `System` (default — follow the OS appearance),
`Light`, `Dark`. An unknown or missing value falls back to `System`.

Restart Duetto for the change to take effect.

## Config directory

`settings.json` lives alongside Duetto's other config (`connections.json`,
`session.json`, …):

| OS | Path |
|----|------|
| macOS | `~/Library/Application Support/Duetto/settings.json` |
| Linux | `$XDG_CONFIG_HOME/duetto/settings.json` or `~/.config/duetto/settings.json` |
| Windows | `%APPDATA%\Duetto\settings.json` |

## Notes

- Applies on next launch (restart-to-apply); there is no in-app toggle.
- `System` follows the OS light/dark appearance as detected at startup.
