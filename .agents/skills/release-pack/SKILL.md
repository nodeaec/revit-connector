---
name: release-pack
description: Package, stage, hash, and install Autodesk Revit plugins for distribution or local testing. Trigger whenever creating a release, packaging the plugin, generating zip archives, building per-year Setup.exe installers, deploying to Revit, or when asked to "package the plugin", "create release", "run release.ps1", "build the installer", "install addin into revit", "deploy plugin to revit", or "generate release zip".
---

# Release Pack

This skill guides packaging and deploying Autodesk Revit plugins into standardized `.zip` distributions and into **one Setup.exe per Revit year**, and installing them locally into Revit's Addins environment.

---

## 🚀 Packaging Workflow (`scripts/release.ps1`)

The repository provides an automated PowerShell release script at `scripts/release.ps1`.

### One year per invocation

`-RevitYear` (default `2026`, supported `2023`..`2027`) selects the **single** Revit
year for that run. Each invocation builds, stages and zips that year, and — when
Inno Setup 6 is available — compiles that year's installer:

- Archive: `release\NodeAec.Connector-<versão>-R<ano>.zip` (unchanged naming)
- Installer: `release\NodeAec.Connector-<versão>-R<ano>-Setup.exe` (+ `.sha256` sidecar)

The Setup installs **only** into `%ProgramData%\Autodesk\Revit\Addins\<ano>\`; it does
not detect or touch other Revit years. Run the script once per year to produce the
five independently downloadable installers.

### 1. Basic Release Packaging (Generates `.zip` + SHA-256, plus the year's Setup.exe when ISCC is present)

```powershell
Set-Location plugin
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 0.1.2 -RevitYear 2026
```

Output:
- Staged folder: `release\stage\NodeAec.Connector\`
- Archive: `release\NodeAec.Connector-0.1.2-R2026.zip`
- Installer: `release\NodeAec.Connector-0.1.2-R2026-Setup.exe` (when Inno Setup 6 is installed)
- Console output: Displays the computed SHA-256 hashes.

### 2. Packaging + Automatic Local Installation

```powershell
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 0.1.2 -RevitYear 2026 -Install
```

This builds, packages, and deploys the add-in to Revit's discovery directories for
the selected year:

- **Manifest**: `C:\ProgramData\Autodesk\Revit\Addins\2026\NodeAec.Connector.addin` (must be at the root of `Addins\<Ano>\` for Revit discovery).
- **Runtime Payload**: `C:\ProgramData\Autodesk\Revit\Addins\2026\NodeAec.Connector\` (assemblies, dependency DLLs, resources).

---

## 🔀 Per-year Installer Identity (`installer.iss`)

- `release.ps1` owns a deterministic year → AppId table and always passes
  `/DAppId={GUID}` to ISCC; `installer.iss` fails the compile when `/DAppId` is
  absent.
- `AppName` is `Node.aec Connector - Revit <ano>` and the Setup is compiled with
  `/DRevitYear=<ano>` (default `2026`); one Setup handles exactly one year.
- Because every year has a distinct AppId, different years install **side by
  side**; Windows Settings > Apps shows one entry per year and uninstalls each
  independently.
- Pre-flight (`PrepareToInstall`) aborts before touching any file when
  `C:\Program Files\Autodesk\Revit\<ano>` does not exist (bilingual message) or
  when `Revit.exe` is running.
- The previous-install prompt (`InitializeSetup`) is scoped to that year's
  AppId only.
- Uninstall removes only that year's `NodeAec.Connector` folder and `.addin`
  manifest.

---

## 🔍 Staging Invariants (What Goes Into the Package)

A compliant release package must contain:

1. **Add-in Manifest (`.addin`)**:
   - Must contain the correct absolute `Assembly` path pointing to the installed DLL for the packaged year: `...\Addins\<ano>\NodeAec.Connector\NodeAec.Connector.dll`.
   - Contains a unique `AddInId` GUID and `FullClassName` matching `App`.
2. **Plugin DLL**:
   - `NodeAec.Connector.dll` (or target plugin DLL).
3. **Runtime Dependencies**:
   - `System.Security.Cryptography.ProtectedData.dll` — **`net48` targets only** (Revit
     2023/2024). On `net8.0-windows`/`net10.0-windows` (Revit 2025+) it is provided by the
     host's `Microsoft.WindowsDesktop.App` runtime and must NOT be staged.
4. **Resources & Assets**:
   - `Resources/` containing PNG icons.
   - `README.md` (human documentation included with the release).
5. **FORBIDDEN Binaries**:
   - `RevitAPI.dll`, `RevitAPIUI.dll`, `AdWindows.dll`, `UIFramework*.dll` must NEVER be included. The script enforces:
     `Where-Object { $_.Name -notlike "RevitAPI*" -and $_.Name -ne "AdWindows.dll" -and $_.Name -notlike "UIFramework*" }`

---

## 🛠️ Script Parameter Reference

| Parameter | Type | Default | Purpose |
|---|---|---|---|
| `-Version` | String | `"0.1.2"` | SemVer release version for the archive and Setup names |
| `-RevitYear` | String | `"2026"` | Single target Autodesk Revit year (`2023`..`2027`) |
| `-Configuration` | String | `"Release"` | Build configuration (`Release` or `Debug`) |
| `-Install` | Switch | `false` | When set, deploys files into `%ProgramData%\Autodesk\Revit\Addins\<Ano>\` |
| `-SkipBuild` | Switch | `false` | Skips `dotnet build` if already compiled |

---

## ✅ Validation Checklist

- [ ] `release.ps1` runs without terminating errors.
- [ ] Staging directory (`release/stage/...`) is clean and free of leftover build artifacts.
- [ ] No `RevitAPI*.dll` assemblies are present in the stage or `.zip` file.
- [ ] `System.Security.Cryptography.ProtectedData.dll` is in the stage and `.zip` **for `net48` (Revit 2023/2024)**, and absent from them for `net8.0-windows`/`net10.0-windows`.
- [ ] The staged `.addin` points at `...\Addins\<ano>\NodeAec.Connector\NodeAec.Connector.dll` for the packaged year.
- [ ] The generated `.zip` has a valid SHA-256 hash printed.
- [ ] The Setup is named `NodeAec.Connector-<versão>-R<ano>-Setup.exe` and its `AppName`/`AppId` identify that single year; two different years' Setups do not collide.
- [ ] When `-Install` is used, the `.addin` and assemblies exist in `C:\ProgramData\Autodesk\Revit\Addins\<Ano>\`.
- [ ] Launching Revit recognizes the newly installed add-in without load warnings.
