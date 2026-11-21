# Pixill: how releases are built and signed

What a downloaded Pixill file can prove about itself today, what it cannot,
and what is prepared for later.

## Today: built in the open, with a signed record

Release files are not built on anyone's own computer. Pushing a tag `vX.Y.Z`
runs [`.github/workflows/build.yml`](../.github/workflows/build.yml) on a
GitHub-hosted Windows machine, which:

1. checks that the tag matches the version in `src\AssemblyInfo.cs`;
2. runs `tools\build-msi.ps1`, the same script anyone can run locally;
3. runs the built program once in snapshot mode against the real data
   services and fails if it does not draw itself;
4. writes `SHA256SUMS.txt`;
5. creates a **build attestation** for the exe and the MSI;
6. publishes the three files as the release.

The attestation is a statement signed through [Sigstore](https://www.sigstore.dev/)
with a short-lived certificate that GitHub issues to that one workflow run,
and it is entered in a public, append-only log. It ties the file's SHA-256 to
the repository, the workflow file, the commit and the run. Nobody holds a
long-lived signing key, including the author, so there is no key to steal.

To check a file (needs the [GitHub CLI](https://cli.github.com/)):

```
gh attestation verify Pixill.exe -R horusscope/pixill
gh attestation verify Pixill-1.0.1.msi -R horusscope/pixill
```

All attestations are also listed at
<https://github.com/horusscope/pixill/attestations>.

**What this proves:** the file you hold is byte-for-byte what this
repository's public build script produced from a specific public commit.

**What it does not prove:** that the source code is safe (read it; it is
about 2,200 lines), or anything Windows itself will act on. Windows does not
look at attestations.

## Not yet: a Windows (Authenticode) signature

SmartScreen and the "Unknown publisher" prompt look only at Authenticode
signatures from a certificate authority Windows trusts. Those certificates
cost money every year, with one exception relevant here:
[SignPath Foundation](https://signpath.org/) signs open-source projects for
free, with its own certificate, if the project meets its
[terms](https://signpath.org/terms). Read on 2026-11-20, the terms that
matter for Pixill are:

| Requirement | Pixill |
|---|---|
| OSI-approved licence, no proprietary parts | Yes: GPL v2, fonts under OFL |
| Built from the public repository by an automated, verifiable build | Yes: the workflow above |
| Product name and version set in the files | Yes: `src\AssemblyInfo.cs`, also used for the MSI |
| Installers announce what they change and can uninstall | Yes: README, "Get it" |
| Privacy statement | Yes: README, "Privacy" |
| Multi-factor sign-in for everyone with repository access | The maintainer's to confirm |
| "Actively maintained and already released", and for executables "a certain verifiable reputation" | **Not yet.** Pixill was first published in November 2026 |

The last row is the open one. Acceptance is at SignPath Foundation's
discretion, and a brand-new program with no users to point to is the case
that requirement exists to turn away. The sensible time to apply is once
there is something to show: some weeks of history, downloads, stars or
issues from people other than the author.

### What is already wired for that day

The workflow contains a SignPath step that is skipped unless the repository
variable `SIGNPATH_ORGANIZATION_ID` exists. After acceptance:

1. In SignPath, create a project with the slug `pixill`, a signing policy
   with the slug `release-signing`, and link the predefined GitHub.com
   trusted build system to it.
2. Give the project an artifact configuration for the uploaded zip that
   signs `Pixill.exe`, the copy of `Pixill.exe` inside the MSI, and then the
   MSI itself:

   ```xml
   <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
     <zip-file>
       <pe-file path="Pixill.exe"><authenticode-sign/></pe-file>
       <msi-file path="Pixill-*.msi">
         <pe-file path="Pixill.exe"><authenticode-sign/></pe-file>
         <authenticode-sign/>
       </msi-file>
     </zip-file>
   </artifact-configuration>
   ```

   (Written from SignPath's documentation and not yet run; expect to adjust
   it the first time.)
3. In the GitHub repository settings, add the variable
   `SIGNPATH_ORGANIZATION_ID` and the secret `SIGNPATH_API_TOKEN`.
4. Add the code signing policy below to the README.
5. Tag a new version. The signed files replace the unsigned ones before the
   checksums and attestations are made, so those then describe the signed
   files. Each signing request is approved by hand in SignPath.

### Code signing policy (to be added to the README on acceptance, not before)

> **Code signing policy.** Free code signing provided by
> [SignPath.io](https://signpath.io/), certificate by
> [SignPath Foundation](https://signpath.org/).
>
> - Committers, reviewers and approvers: [horusscope](https://github.com/horusscope)
> - Privacy: see [Privacy](../README.md#privacy).

The publisher Windows shows would then be "SignPath Foundation", not Pixill
or its author. That is how their programme works.

## Options that were considered and set aside

- **A self-signed certificate.** Free, and worth nothing on anyone else's
  computer: Windows trusts it only where it has been installed by hand.
- **A paid certificate or Microsoft's Trusted Signing service.** They work,
  and they are a yearly or monthly fee for a free program.
- **The Microsoft Store.** The Store signs what it distributes, but that
  means repackaging as MSIX and a Store listing; a different project.
