# Releasing

`.github/workflows/release.yml` builds the packages on GitHub.

## Make a release

```bash
git tag v1.0.0
git push origin v1.0.0
```

The workflow runs the tests, builds the packages and publishes them on the repository's **Releases** page:

| File | For |
|---|---|
| `Pardeh-<version>-win-x64-standalone.zip` | Windows 10 / 11, x64, with .NET included |
| `Pardeh-<version>-win-x64.zip` | the smaller Windows package (needs the .NET 11 Desktop Runtime) |
| `Pardeh-<version>-arm64-v8a.apk` | Android 7.0 or newer (only with the signing secrets below) |
| `SHA256SUMS.txt` | checksums |

If a job fails, nothing is published. A tag has the form `vX.Y.Z`, each number from 0 to 999. The Android version code is
`X*1000000 + Y*1000 + Z`, so use a higher version for every release.

The Windows 11 Widgets board widget is not part of these packages.

## Try the build without releasing

**Actions > Release > Run workflow** builds everything and releases nothing; download the packages from the run's page.
The same happens when a push to `main` changes the workflow, the packaging scripts or the tests. These builds are numbered
0.0.1.

## Android signing

Android installs an update only when it is signed with the same key as the installed app, so every release must use your
one release key. Add these under **Settings > Secrets and variables > Actions > New repository secret**:

| Secret | Value |
|---|---|
| `ANDROID_KEYSTORE_BASE64` | the keystore file, base64 encoded |
| `ANDROID_KEYSTORE_PASSWORD` | the store password |
| `ANDROID_KEY_ALIAS` | the key alias |
| `ANDROID_KEY_PASSWORD` | the key password |

The last three are `storePassword`, `keyAlias` and `keyPassword` in your `keystore.properties`. To put the encoded
keystore on the clipboard in PowerShell:

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("path\to\release.keystore")) | Set-Clipboard
```

Never commit the keystore or its passwords. Anyone who can push a workflow to the repository can use these secrets, so give
write access only to people you trust with the key. Keep your own backup of the keystore: GitHub does not show a secret again.
