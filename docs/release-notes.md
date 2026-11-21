The program is the same as 1.0.0. What changed is where it is built.

From this version on, the release files are built by GitHub Actions from the tagged commit, not on the author's machine, and each one carries a signed build attestation: a public, tamper-evident record that this exact file was produced by this repository's workflow from this commit.

**Check a download** (needs the [GitHub CLI](https://cli.github.com/)):

```
gh attestation verify Pixill.exe -R horusscope/pixill
```

or compare its SHA-256 with `SHA256SUMS.txt`:

```
certutil -hashfile Pixill.exe SHA256
```

**What this is not:** the files do not yet carry a Windows (Authenticode) signature, so SmartScreen may still warn on first run. See [docs/signing.md](https://github.com/horusscope/pixill/blob/main/docs/signing.md) for what is in place and what is pending.

| File | Use |
|---|---|
| `Pixill.exe` | The whole program in one file. Run it from anywhere. |
| `Pixill-1.0.1.msi` | Installer: Program Files, Start menu entry, starts with Windows. Upgrades 1.0.0 in place. |
