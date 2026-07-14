# Beta Release Workflow

The `.github/workflows/beta-release.yml` workflow builds and publishes a prerelease beta package whenever `master` is pushed, or when the workflow is run manually from GitHub Actions.

## What It Does

1. Checks out the repository on `windows-latest`.
2. Installs .NET `10.0.x` and MSBuild.
3. Restores and runs unit tests in a job with no OAuth or signing secrets.
4. Materializes only the Google OAuth build input and builds an unsigned x64 MSIX package.
5. Uploads the unsigned package as a one-day internal workflow artifact.
6. Enters the protected `beta-release` environment, materializes the signing certificate under the runner temporary directory, signs and verifies the MSIX, and removes the certificate.
7. Uploads the signed zip as a workflow artifact.
8. Creates a GitHub prerelease with a unique tag like `beta-v1.3.0.0-123.1`.

## Required Secrets

Set this repository secret for the package job:

- `TASK_FLYOUT_GOOGLE_CREDENTIALS_JSON`: the full contents of the local `credentials.json` file used by Google OAuth.

Create a GitHub Environment named `beta-release`, configure required reviewers or equivalent deployment protection, and set these environment secrets:

- `TASK_FLYOUT_CERTIFICATE_BASE64`: base64-encoded `.pfx` signing certificate bytes.
- `TASK_FLYOUT_CERTIFICATE_PASSWORD`: password for the `.pfx` signing certificate.

To create the certificate secret locally in PowerShell:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("Task_Flyout_TemporaryKey.pfx"))
```

## Release Versioning

The package version is read from `Package.appxmanifest` at build time. Every push creates a new prerelease tag using the manifest version, GitHub run number, and run attempt, so repeated beta builds for the same app version do not overwrite each other.

## Notes

- The workflow only packages `win-x64` for beta releases.
- It intentionally fails if the required secrets are missing rather than producing an unsigned or nonfunctional package.
- Restore and tests never receive OAuth credentials, the signing certificate, or its password.
- Application compilation receives the OAuth public-client configuration but never receives signing material.
- Only the protected release job receives `contents: write` and signing secrets.
- Stable releases should continue to use the normal release process if a separate signing, changelog, or store submission flow is needed.
