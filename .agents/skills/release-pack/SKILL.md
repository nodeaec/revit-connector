---
name: release-pack
description: Package, stage, hash, and install Autodesk Revit plugins for distribution or local testing. Trigger whenever creating a release, packaging the plugin, generating zip archives, building per-group (Revit compatibility groups) Setup.exe installers, deploying to Revit, or when asked to "package the plugin", "create release", "run release.ps1", "build the installer", "install addin into revit", "deploy plugin to revit", or "generate release zip".
---

# Release Pack

This skill guides packaging and deploying Autodesk Revit plugins into standardized `.zip` distributions and into **one Setup.exe per Revit compatibility group** (`2023-2024`, `2025-2026`, `2027` — each Setup covers every installed year of its group), and installing them locally into Revit's Addins environment.

---

## 🚀 Packaging Workflow (`scripts/release.ps1`)

The repository provides an automated PowerShell release script at `scripts/release.ps1`.

### One compatibility group per invocation

`-RevitYear` accepts a compatibility group (`2023-2024`, `2025-2026`, `2027`;
default `2025-2026`) or a single year (`2023`..`2027`, resolved to its group).
Each invocation builds, stages and zips that group, and — when Inno Setup 6 is
available — compiles the group's installer:

- Archive: `release\NodeAec.Connector-<versão>-R<grupo>.zip`
- Installer: `release\NodeAec.Connector-<versão>-R<grupo>-Setup.exe` (+ `.sha256` sidecar)

The payload is always compiled with the group's floor year (`2023`, `2025` or
`2027`): compiling against the oldest API of the group is what makes a single DLL
load on every year of the group. The Setup installs into
`%ProgramData%\Autodesk\Revit\Addins\<ano>\` for **every year of the group present
on the machine** (a year without Revit installed is skipped) and aborts before
touching any file when no year of the group is installed. Run the script once per
group to produce the three independently downloadable installers.

During the wizard the user picks the Revit version from a dropdown (only the group's
years installed on the machine, plus "all installed versions", the default). Silent
installs skip the UI and target every installed year; pass `/RevitYear=<year>` to target
one year. The uninstaller shows the same list (years where the add-in is present; only when
there is more than one) and can remove a single year while keeping the Apps entry
for the remaining ones.

### 1. Basic Release Packaging (Generates `.zip` + SHA-256, plus the group's Setup.exe when ISCC is present)

```powershell
# Run from the repository root
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 0.1.2 -RevitYear 2025-2026
```

Output:
- Staged folder: `release\stage\NodeAec.Connector\`
- Archive: `release\NodeAec.Connector-0.1.2-R2025-2026.zip`
- Installer: `release\NodeAec.Connector-0.1.2-R2025-2026-Setup.exe` (when Inno Setup 6 is installed)
- Console output: Displays the computed SHA-256 hashes.

### 2. Packaging + Automatic Local Installation

```powershell
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 0.1.2 -RevitYear 2025-2026 -Install
```

This builds, packages, and deploys the add-in to Revit's discovery directories for
every year of the group installed locally (the example below shows only 2026):

- **Manifest**: `C:\ProgramData\Autodesk\Revit\Addins\2026\NodeAec.Connector.addin` (one manifest per installed year, at the root of `Addins\<Ano>\` for Revit discovery).
- **Runtime Payload**: `C:\ProgramData\Autodesk\Revit\Addins\2026\NodeAec.Connector\` (assemblies, dependency DLLs, resources).

---

## 🔀 Per-group Installer Identity (`installer.iss`)

- `release.ps1` owns a deterministic year → AppId table. The group's Setup uses
  the **floor year's** AppId as its identity (so it upgrades older per-year
  installs of that year) and passes every AppId of the group through
  `/DPreviousAppIds`; `installer.iss` fails the compile when `/DAppId` or
  `/DRevitYears` is absent.
- `AppName` is `Node.aec Connector - Revit <grupo>`; the Setup is compiled with
  `/DRevitYears=<anos>` and `/DGroupLabel=<grupo>`, and one Setup covers the
  whole group.
- Because every group keeps a distinct AppId, different groups install **side by
  side**; Windows Settings > Apps shows one entry per group and uninstalls each
  independently.
- Pre-flight (`PrepareToInstall`) aborts before touching any file when **no**
  year of the group is installed under `C:\Program Files\Autodesk\Revit <ano>`
  (bilingual message) or when `Revit.exe` is running.
- The `Revit version` wizard page lists the installed years of the group
  (dropdown; page skipped when only one year is installed).
- The previous-install prompt (`InitializeSetup`) scans every AppId of the group,
  so older per-year installs (a 2024-only or 2026-only Setup) are detected and
  can be uninstalled.
- The post-install copy (`CurStepChanged`) writes the payload folder + `.addin`
  manifest into every installed year of the group.
- Uninstall asks which year to remove (dropdown of the years where the add-in is
  present; skipped when only one year has it) or removes everything: a single-year removal keeps the Apps entry so
  the remaining years can be removed later; "all versions" and silent runs remove
  the `NodeAec.Connector` folder and `.addin` manifest from every year of the group.

---

## 🔍 Staging Invariants (What Goes Into the Package)

A compliant release package must contain:

1. **Add-in Manifest (`.addin`)**:
   - The staged manifest (only shipped inside the `.zip`) contains the absolute `Assembly` path of the build year: `...\Addins\<ano-base>\NodeAec.Connector\NodeAec.Connector.dll`. The Setup does NOT use it: `[Code]` generates one manifest per installed year at run time.
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
| `-RevitYear` | String | `"2025-2026"` | Revit compatibility group (`2023-2024`, `2025-2026`, `2027`) or a single year (`2023`..`2027`, resolved to its group) |
| `-Configuration` | String | `"Release"` | Build configuration (`Release` or `Debug`) |
| `-Install` | Switch | `false` | When set, deploys files into `%ProgramData%\Autodesk\Revit\Addins\<Ano>\` for every year of the group installed locally |
| `-SkipBuild` | Switch | `false` | Skips `dotnet build` if already compiled |

---

## ✅ Validation Checklist

- [ ] `release.ps1` runs without terminating errors.
- [ ] Staging directory (`release/stage/...`) is clean and free of leftover build artifacts.
- [ ] No `RevitAPI*.dll` assemblies are present in the stage or `.zip` file.
- [ ] `System.Security.Cryptography.ProtectedData.dll` is in the stage and `.zip` **for `net48` (Revit 2023/2024)**, and absent from them for `net8.0-windows`/`net10.0-windows`.
- [ ] The staged `.addin` points at `...\Addins\<ano-base>\NodeAec.Connector\NodeAec.Connector.dll` (the group's floor year).
- [ ] The generated `.zip` has a valid SHA-256 hash printed.
- [ ] The Setup is named `NodeAec.Connector-<versão>-R<grupo>-Setup.exe` and its `AppName`/`AppId` identify that group; different groups' Setups do not collide.
- [ ] The wizard shows the `Revit version` dropdown (skipped when only one year is
  installed) and the uninstaller asks which year to remove.
- [ ] When `-Install` is used, the `.addin` and assemblies exist in `C:\ProgramData\Autodesk\Revit\Addins\<Ano>\` for every year of the group installed locally.
- [ ] Launching Revit recognizes the newly installed add-in without load warnings.
