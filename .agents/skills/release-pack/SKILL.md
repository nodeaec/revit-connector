---
name: release-pack
description: Package, stage, hash, and install the Node.aec Connector as an Autodesk .bundle for distribution or local testing. Trigger whenever creating a release, packaging the plugin, generating the bundle zip, building the Setup.exe installer, deploying to Revit, or when asked to "package the plugin", "create release", "run release.ps1", "build the installer", "install addin into revit", "deploy plugin to revit", or "generate release zip".
---

# Release Pack

One run of `scripts/release.ps1` builds every Revit compatibility group and produces
**one Autodesk bundle** and **one Setup.exe** for Revit 2023–2027.

---

## 🧱 Bundle layout

```
NodeAec.Connector.bundle\
  PackageContents.xml          one <Components> per Revit year (SeriesMin = SeriesMax = R<year>)
  README.md
  Contents\2023-2024\          net48            + NodeAec.Connector.addin + DLLs + Resources\
  Contents\2025-2026\          net8.0-windows   + NodeAec.Connector.addin + DLLs + Resources\
  Contents\2027\               net10.0-windows  + NodeAec.Connector.addin + DLLs + Resources\
```

- Each payload is compiled with its group's floor year (2023, 2025, 2027): compiling
  against the oldest API of the group is what lets one DLL load on every year of it.
- Each `Contents\<group>\NodeAec.Connector.addin` is the repo's
  `src/NodeAec.Connector/NodeAec.Connector.addin`, whose `Assembly` path is **relative**
  (`NodeAec.Connector.dll`), so it works wherever the bundle is installed.
- Revit reads `%ProgramData%\Autodesk\ApplicationPlugins\*.bundle\PackageContents.xml`
  at startup and loads only the component matching its version. A year without a
  component does not load the Connector.
- `ProductCode` / `UpgradeCodes` in `PackageContents.xml` are fixed; they live in
  `release.ps1` and `installer.iss` and must stay identical.

---

## 🚀 `scripts/release.ps1`

```powershell
# From the repository root
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 0.3.0
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 0.3.0 -Install
```

Output in `release\`:
- `stage\NodeAec.Connector.bundle\` — the staged bundle, every year enabled
- `NodeAec.Connector-<version>.bundle.zip` (+ `.sha256`) — the bundle for manual / IT deployment: extract into `%ProgramData%\Autodesk\ApplicationPlugins\`
- `NodeAec.Connector-<version>-Setup.exe` (+ `.sha256`) — when Inno Setup 6 is installed

`-Install` (developer machine): removes legacy `Addins\<year>\NodeAec.Connector*` installs,
then copies the whole bundle to `%ProgramData%\Autodesk\ApplicationPlugins\`. It refuses to
run while Revit is open and warns when old installer entries remain in Windows Apps.

| Parameter | Default | Purpose |
|---|---|---|
| `-Version` | *(required)* | Must equal `<Version>` in `NodeAec.Connector.csproj` (the script reads it and fails on a mismatch) |
| `-Configuration` | `Release` | Build configuration |
| `-Install` | off | Install the staged bundle locally (every year) |
| `-SkipBuild` | off | Reuse existing `bin\<year>\` outputs |

The script fails when a payload misses `NodeAec.Licensing.Lite.dll` or
`BouncyCastle.Cryptography.dll`, when the net48 payload misses
`System.Security.Cryptography.ProtectedData.dll`, or when a net8/net10 payload has it.

---

## 🧩 Installer (`scripts/installer.iss`)

- **One AppId** (`{1A92A811-32B9-4BBE-8643-7A1EEEDE13BB}`), one entry in Windows
  Settings > Apps: *Node.aec Connector*. Installs into
  `%ProgramData%\Autodesk\ApplicationPlugins\NodeAec.Connector.bundle\`; the uninstaller
  lives in `%ProgramFiles%\Node.aec Connector\Uninstall\`, outside the folder Revit scans.
- **Year page:** one checkbox per Revit year 2023–2027, each marked *installed* / *not
  installed*. Defaults: the previous choice on a re-run, otherwise every installed year.
  At least one year must be checked. Re-running Setup is how years are added or removed.
- Only the payloads of checked groups are copied; `[Code]` writes `PackageContents.xml`
  with one component per checked year and deletes payloads whose years were all unchecked.
- **Silent:** `/VERYSILENT /RevitYears=2025,2026`. Without `/RevitYears` it keeps the
  previous choice, or every installed year; with nothing to install it fails (no files touched).
- **Migration:** `PrepareToInstall` silently runs every uninstaller whose Apps entry is
  named `Node.aec Connector - Revit …` (the per-group installers, any AppId) and deletes
  any leftover `%ProgramData%\Autodesk\Revit\Addins\<year>\NodeAec.Connector*`, so Revit
  never loads two Connectors.
- Setup and uninstall refuse to run while `Revit.exe` is running. With `/WaitForRevit`
  (what the Connector's **Baixar e instalar** passes, with `/SILENT`) Setup instead shows
  *Feche o Revit para continuar a instalacao.* and waits for Revit to exit; Cancel stops the
  wait and Setup ends without touching files.
- `/DAppVersion` and `/DAppVersionNum` are required; `release.ps1` passes them.

---

## 📤 Publish the release on the platform

The Connector's **Verificar atualizações** reads the platform, not GitHub. After building:

1. On nodeaec.com.br, add a version `<x.y.z>` to the product `nodeaec-connector`, with a
   short pt-BR description (shown as the update notes), and upload
   `NodeAec.Connector-<x.y.z>-Setup.exe` as its **default** file. Check that the stored
   SHA-256 equals the one `release.ps1` printed.
2. Publish the version with download allowed.
3. Only when old Connectors must stop working (a key rotation, a breaking API change), raise
   the product's minimum version. Those users then get `CONNECTOR_OUTDATED`.

---

## ✅ Validation checklist

- [ ] `release.ps1 -Version <x.y.z>` runs without errors and prints the zip and Setup hashes.
- [ ] `release\stage\NodeAec.Connector.bundle\PackageContents.xml` lists 2023–2027, each pointing at the right `Contents\<group>\NodeAec.Connector.addin`.
- [ ] No `RevitAPI*.dll`, `AdWindows.dll` or `UIFramework*.dll` anywhere in the bundle.
- [ ] `ProtectedData.dll` only in `Contents\2023-2024\`.
- [ ] Setup on a machine with an old per-group install: the old Apps entries and `Addins\<year>\NodeAec.Connector*` are gone afterwards; only *Node.aec Connector* remains.
- [ ] Unchecking a year and re-running Setup removes that year's component; Revit of that year no longer shows the Node.aec tab.
- [ ] Revit of each checked year shows the **Node.aec** tab without load errors.
- [ ] With Revit open, `Setup.exe /SILENT /WaitForRevit` shows the wait message, and installs as soon as Revit closes; Cancel leaves the install untouched.
- [ ] After publishing, an older Connector's **Verificar atualizações** offers the new version, and **Baixar e instalar** completes.
