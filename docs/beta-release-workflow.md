# Beta Release Workflow

The `.github/workflows/beta-release.yml` workflow builds and publishes a prerelease beta package whenever `master` is pushed, or when the workflow is run manually from GitHub Actions.

## What It Does

1. Checks out the repository on `windows-latest`.
2. Installs .NET `10.0.x` and MSBuild.
3. Restores and runs Release-configuration unit tests as the publication gate, with no OAuth or signing secrets.
4. Gives the MSIX a unique, upgradeable CI revision, materializes only the Google OAuth build input, and builds an unsigned x64 package.
5. Uploads the unsigned package as a one-day internal workflow artifact.
6. Enters the protected `beta-release` environment, materializes the signing certificate under the runner temporary directory, signs and verifies the MSIX, and removes the certificate.
7. Generates update notes from every commit since the previous published GitHub release.
8. Creates a GitHub prerelease containing a zip with the signed MSIX, public certificate, and `Install.ps1`.

## Required Secrets

Set this repository secret for the package job:

- `TASK_FLYOUT_GOOGLE_CREDENTIALS_JSON`: the full contents of the local `credentials.json` file used by Google OAuth.
- `TASK_FLYOUT_MICROSOFT_CLIENT_ID`: the Microsoft public-client application ID used to generate the ignored `Secrets.cs` build input.

Create a GitHub Environment named `beta-release` and set these environment secrets. Do not require reviewers if every `master` push must publish automatically:

- `TASK_FLYOUT_CERTIFICATE_BASE64`: base64-encoded `.pfx` signing certificate bytes.
- `TASK_FLYOUT_CERTIFICATE_PASSWORD`: password for the `.pfx` signing certificate.

To create the certificate secret locally in PowerShell:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("Task_Flyout_TemporaryKey.pfx"))
```

## Release Versioning

The workflow reads the first three version fields from `Package.appxmanifest` and uses the GitHub run number as the fourth MSIX revision. For example, manifest base `1.3.1.0` at run 123 produces package `1.3.1.123` and tag `beta-v1.3.1.123-123.1`. Both GitHub assets and Windows package upgrades are therefore unique.

Release notes list the complete commit range from the previous published release tag through the pushed SHA. Serialized workflow concurrency keeps signing and publication from racing. If rapid pushes replace an older pending run, its commits remain in the next release's aggregated range.

## Local Sideload Packages

Local packaging is unsigned by default because Store packages are signed after certification and CI signs beta artifacts in its protected release job. Before installing a locally generated MSIX, sign it with the local sideload helper:

```powershell
.\scripts\sign-sideload-package.ps1 `
  -PackagePath ".\AppPackages\Task_Flyout_1.3.0.0_x64_Test\Task_Flyout_1.3.0.0_x64.msix" `
  -TrustMachine
```

The first `-TrustMachine` run creates a non-exportable current-user test signing key and requests UAC approval to trust only its public certificate in `LocalMachine\TrustedPeople`. Later packages can be signed with the same command; machine trust is reused. This certificate is only for local testing and must not be used for public releases.

The certificate subject is read from `Package.appxmanifest`, and the script fails if signing or verification is unsuccessful. Re-run the helper whenever packaging overwrites the MSIX.

For the normal local test loop, use the combined helper instead. It selects the newest x64 Task Flyout package, signs it after every rebuild, closes a running tray instance, installs the matching Windows App Runtime dependency, and verifies the deployed package:

```powershell
.\scripts\install-latest-sideload-package.ps1
```

To make Visual Studio or `dotnet msbuild` `SideloadOnly` packaging sign automatically, configure the current machine once:

```powershell
.\scripts\configure-local-sideload-signing.ps1 -TrustMachine
```

This writes the Git-ignored `Directory.Build.local.props` with the matching current-user certificate thumbprint. Store and CI builds remain unsigned until their separate protected signing stage.

## Notes

- The workflow only packages `win-x64` for beta releases.
- It intentionally fails if the required secrets are missing rather than producing an unsigned or nonfunctional package.
- Restore and tests never receive OAuth credentials, the signing certificate, or its password.
- Application compilation receives the OAuth public-client configuration but never receives signing material.
- Only the protected release job receives `contents: write` and signing secrets.
- The one-day unsigned artifact is internal job transport only; public releases contain only the signed install bundle.
- Stable releases should continue to use the normal release process if a separate signing, changelog, or store submission flow is needed.
