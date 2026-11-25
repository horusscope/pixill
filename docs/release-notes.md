**Fixed: Win+D (Show desktop) hid the widget.** Pixill now stays visible on the desktop when you show it, and drops back underneath your windows when they return. The cause and the fix are described in [docs/design.md](https://github.com/horusscope/pixill/blob/main/docs/design.md#one-window-pinned-to-the-bottom).

Nothing else changed.

**Check a download** (needs the [GitHub CLI](https://cli.github.com/)):

```
gh attestation verify Pixill.exe -R horusscope/pixill
```

or compare its SHA-256 with `SHA256SUMS.txt` (`certutil -hashfile Pixill.exe SHA256`). The files are built by GitHub Actions from the tagged commit and carry a signed build attestation; they do not carry a Windows (Authenticode) signature, so SmartScreen may warn on first run. See [docs/signing.md](https://github.com/horusscope/pixill/blob/main/docs/signing.md).

| File | Use |
|---|---|
| `Pixill.exe` | The whole program in one file. Run it from anywhere. |
| `Pixill-1.0.2.msi` | Installer: Program Files, Start menu entry, starts with Windows. Upgrades earlier versions in place. |
