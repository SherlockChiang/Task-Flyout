# Beta Release Workflow

The `.github/workflows/beta-release.yml` workflow builds and publishes a prerelease beta package whenever `master` is pushed, or when the workflow is run manually from GitHub Actions.

## What It Does

1. Checks out the repository on `windows-latest`.
2. Installs .NET `10.0.x` and MSBuild.
3. Writes required local-only build inputs from GitHub Secrets.
4. Runs the unit tests.
5. Builds a signed x64 MSIX package.
6. Uploads a zipped package as a workflow artifact.
7. Creates a GitHub prerelease with a unique tag like `beta-v1.3.0.0-123.1`.

## Required Secrets

Set these repository secrets before relying on the workflow:

- `TASK_FLYOUT_GOOGLE_CREDENTIALS_JSON`: the full contents of the local `credentials.json` file used by Google OAuth.
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
- Stable releases should continue to use the normal release process if a separate signing, changelog, or store submission flow is needed.
