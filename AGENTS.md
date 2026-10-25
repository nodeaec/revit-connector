# AGENTS.md — Engineering and Governance Guidelines for AI Agents

Canonical engineering guide for autonomous AI agents (Antigravity, Claude Code, Cursor, OpenCode, Copilot) that develop, maintain, or refactor code inside the **`nodeaec/revit-connector`** repository.

Official repository: [github.com/nodeaec/revit-connector](https://github.com/nodeaec/revit-connector)

---

## 🎯 Repository Scope and Mission

The `revit-connector` repository maintains the **Node.aec Connector** — the official desktop governance, licensing, and unified-Ribbon add-in from **Node.aec** for Autodesk Revit — plus the partner-facing integration docs. Partner plugins integrate through the
`NodeAec.Licensing.Lite` NuGet package (repository `revit-licensing-lite`), not through code from this repository.
The goal is to accelerate the AEC/BIM developer ecosystem by standardizing integration with the Node.aec platform (licensing, catalog, updates) and serving as an engineering reference for professional plugins.

### Primary Project in the Repository

1. **`NodeAec.Connector`**:
   - The **central desktop governance Hub** and unified Ribbon for Node.aec in Autodesk Revit.
   - Manages browser SSO authentication (RFC 8252 loopback), master entitlements lease sync (`entitlements.lease`), and the user interface for manual NAEC key activation.
   - Verifies leases and computes the Machine ID with the `NodeAec.Licensing.Lite` package, the same code partner plugins run (`src/NodeAec.Connector/Cryptography/LeaseTrust.cs`, `docs/lite-adoption.md`). It keeps no gate, verifier, or trust anchor of its own.
   - Manages the canonical **`Node.aec`** tab and deduplication via `Autodesk.Windows.ComponentManager`.

> [!NOTE]
> If your goal is to explain how to integrate Node.aec licensing into an **external user plugin**, use the canonical [`licensing-integrate` skill in the Sample Plugin](https://github.com/nodeaec/revit-sample-plugin/tree/main/.agents/skills/licensing-integrate) (this repo's copy is only a pointer to it) and the [Partner Integration Guide](docs/PARTNER_INTEGRATION.md). This file governs **internal development of this repository**.

---

## 🧩 Modular Skills (.agents/skills/)

This repository ships specialized modular skills for autonomous AI agents. Invoke the skill matching the task goal:

### 1. Revit Domain & Tooling
| Skill | Scope and Activation Triggers | Canonical Path |
|---|---|---|
| **`licensing-integrate`** | Pointer to the canonical partner skill in `revit-sample-plugin` (new plugin, existing plugin, learning from the sample). Partner-facing changes go there, not here. | [`.agents/skills/licensing-integrate`](.agents/skills/licensing-integrate/SKILL.md) |
| **`ribbon-guard`** | Create/modify Ribbon panels and buttons, enforce the `Node.aec` tab, non-blocking icons, and deduplication via AdWindows. | [`.agents/skills/ribbon-guard`](.agents/skills/ribbon-guard/SKILL.md) |
| **`revit-build-validate`** | Build via `dotnet build`, enforce 0 errors, isolate RevitAPI DLLs, and validate runtime dependencies. | [`.agents/skills/revit-build-validate`](.agents/skills/revit-build-validate/SKILL.md) |
| **`release-pack`** | Package the `.bundle` zip and the single Setup.exe (Revit 2023–2027) via `release.ps1`, verify SHA-256, and install into local Revit (`%ProgramData%\Autodesk\ApplicationPlugins`). | [`.agents/skills/release-pack`](.agents/skills/release-pack/SKILL.md) |

### 2. Software Engineering, Quality & Workflow
| Skill | Scope and Activation Triggers | Canonical Path |
|---|---|---|
| **`clean-code-and-oop`** | Clean Code patterns in C#, SOLID, SRP, early returns, and decoupling Revit UI from domain rules. | [`.agents/skills/clean-code-and-oop`](.agents/skills/clean-code-and-oop/SKILL.md) |
| **`document-touched-code`** | XML documentation (`/// <summary>`, `<param>`, `<returns>`) on C# members and intent-revealing comments. | [`.agents/skills/document-touched-code`](.agents/skills/document-touched-code/SKILL.md) |
| **`security-defense-and-mitigation`** | Ed25519 SPKI cryptography, DPAPI (`CurrentUser`), SHA-256 machine lock, HTTPS, and fail-closed protection. | [`.agents/skills/security-defense-and-mitigation`](.agents/skills/security-defense-and-mitigation/SKILL.md) |
| **`code-review`** | Two-axis code review (Revit Patterns + functional spec) with auditor subagents. | [`.agents/skills/code-review`](.agents/skills/code-review/SKILL.md) |
| **`test-first-delivery`** | Test-driven development (IV-TDD) in C# for headless logic with no Revit UI dependency. | [`.agents/skills/test-first-delivery`](.agents/skills/test-first-delivery/SKILL.md) |
| **`git-change-workflow`** | Branch strategy (Fast Track vs Planned Track), atomic commits, and inspection before staging. | [`.agents/skills/git-change-workflow`](.agents/skills/git-change-workflow/SKILL.md) |
| **`semantic-commit`** | Format and execute standardized semantic commits with Revit-plugin scopes (`<type>(<scope>): <summary>`). | [`.agents/skills/semantic-commit`](.agents/skills/semantic-commit/SKILL.md) |

### 3. Orca Workspace Tools
Installed from [`stablyai/orca`](https://github.com/stablyai/orca) and pinned in `skills-lock.json`. Each is a short stub that loads its version-matched guide from the Orca executable, so update them with Orca, not by hand: `orca-cli`, `orchestration`, `computer-use`, `orca-jira`, `orca-linear` (and its legacy name `linear-tickets`), `jira-tickets`, `orca-emulator`, `orca-emulator-android`, `orca-per-workspace-env`.

---

## 🏗️ Technology Stack and Runtimes

- **Language**: C# 12
- **Target Framework**: per-Revit-year matrix — `net48` (2023/2024), `net8.0-windows` (2025/2026), and `net10.0-windows` (2027)
- **Host Application**: Autodesk Revit 2023–2027 (one build per compatibility group, shipped together as one `.bundle` with one installer)
- **Graphical Interface**: WPF (`UseWPF = true`), clean C# code with native layouts
- **Data Protection**: Windows DPAPI (`System.Security.Cryptography.ProtectedData`)
- **Asymmetric Cryptography**: Ed25519 (EdDSA / RFC 8032) for offline validation of signed leases
- **Build System**: .NET CLI (`dotnet build`, `dotnet test`) and PowerShell packaging scripts (`scripts/release.ps1`)

---

## 🛡️ Repository Engineering Rules

### 1. Revit Ribbon: Mandatory Canonical `Node.aec` Tab
- **Single Tab**: Every tool, add-in, and component created in this repository **MUST** be added exclusively on the **`Node.aec`** tab (`TabName = "Node.aec"`).
- **No Fragmented Tabs**: Never create separate tabs for individual plugins. Organize features into themed panels inside `Node.aec` (e.g. `"Conector"`, `"Licenciamento"`, etc.).
- **Tab Deduplication**: Use the `Autodesk.Windows.ComponentManager` (AdWindows) hooks to avoid duplicate tabs or ghost panels when reloading add-ins.

### 2. Revit Dependencies and Binaries
- **Never Copy Revit DLLs**: References to `RevitAPI.dll`, `RevitAPIUI.dll`, and `AdWindows.dll` must always carry `<Private>false</Private>`.
- **Dependency Assemblies**: Extra NuGet packages must be packed into the add-in directory using `<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>`. `System.Security.Cryptography.ProtectedData.dll` is payload **only for the `net48` target** (Revit 2023/2024), where only the NuGet package provides it; on `net8.0-windows` and `net10.0-windows` (Revit 2025+) it already ships in the `Microsoft.WindowsDesktop.App` runtime and **must not** be copied into the add-in directory.
- **KISS & Zero Bloat**: Prefer .NET 8 BCL libraries and stock Revit namespaces. Avoid heavy third-party libraries (such as Newtonsoft.Json — use `System.Text.Json`).

### 3. Node.aec Platform Integration
- **Fixed Production Endpoint**: All calls to the Node.aec API use the official production endpoint: `https://api.nodeaec.com.br`.
- **Key Security**: NEVER store private keys in client code. The trust anchors (Ed25519 SPKI public keys) live only in `NodeAec.Licensing.Lite`; this repository compiles none (`ConnectorConfigTests.AddIn_CompilesNoTrustAnchor`). A key rotation ships a new Lite version and a Connector release that references it.
- **Non-Blocking (UI Thread Safe)**: No network call or heavy I/O may run synchronously on Revit's main thread (`OnStartup` or command startup). Background initialization must be async and resilient to network failure.

### 4. Code Structure and Style
- **Clean Code & OOP**: Cohesive classes, concise single-responsibility methods, and explicit exception handling.
- **Nullable Types**: `<Nullable>enable</Nullable>` is on. Resolve possible-null-reference warnings safely.
- **Code Documentation**: Keep XML comments (`/// <summary>`) on public classes, extension methods, and interfaces.

---

## 🛠️ Build and Validation Commands

All changes must be validated by building the relevant solution and confirming zero errors:

```powershell
# 1. Node.aec Connector (Central Hub) — at the repository root
# Build and run headless unit tests
dotnet build NodeAec.Connector.sln -c Release
dotnet test NodeAec.Connector.sln -c Release

# Build every Revit group, package the bundle + Setup.exe, and install the bundle locally
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 0.3.0 -Install
```

> [!WARNING]
> **DPAPI coverage is manual.** `ProtectedData.Protect/Unprotect(..., DataProtectionScope.CurrentUser)` only works in a real logon session (SessionId ≥ 1). In an SSH/`services.exe` context the process runs in Session 0 with no Logon SID (`S-1-5-5-*`) and no `AuthenticationId`, and Windows returns `win32 = 5 Access denied` — the behavior is *fail-closed* by design. That path has no unit-test coverage, so `dotnet test` must come back **100% green in any session (SSH or interactive)**. DPAPI persistence (writing/reading `entitlements.lease` and `session.json` under `%APPDATA%`) is validated manually in the in-Revit add-in smoke test. Do not weaken storage or mark tests as Skip to compensate.

Acceptance Criteria for Changes:
- Clean build: **0 Errors**.
- Unit tests: 100% passing.
- No Revit assemblies (`RevitAPI*.dll`) inside `release/` or `stage/`.
- `System.Security.Cryptography.ProtectedData.dll` must be present in the final add-in payload **only when targeting `net48`** (Revit 2023/2024). On `net8.0-windows`/`net10.0-windows` it is provided by `Microsoft.WindowsDesktop.App` and must not appear in `release/` or `stage/`.

---

## 🗓️ Facts That Go Stale, and Where to Check Them

Versions are repeated across code and docs. Before trusting one, or after changing one,
check its source of truth, then find every repeat with `grep -rn "<old value>" .`
(excluding `bin/`, `obj/`, `release/`).

| Fact | Source of truth | Also written in |
|---|---|---|
| Connector version | `<Version>` in `src/NodeAec.Connector/NodeAec.Connector.csproj` | `ConnectorConfig.Version` (a test enforces it), the `release.ps1 -Version` examples here, in `README.md` and in `.agents/skills/release-pack/SKILL.md` |
| Lite version the Connector ships | `PackageReference` in `src/NodeAec.Connector/NodeAec.Connector.csproj` (and the tests csproj). Partner plugins must build against this version or older, so raise it before partners need a newer Lite | `docs/PARTNER_INTEGRATION.md`, `docs/lite-adoption.md`; the sample and template csproj in their repos |
| Latest Lite | <https://www.nuget.org/packages/NodeAec.Licensing.Lite> | |
| Lite API (`Snapshot`, `GateReason`, messages) | [licensing-lite](https://github.com/nodeaec/licensing-lite) `src/NodeAec.Licensing.Lite/` | `docs/PARTNER_INTEGRATION.md` (reason table) |
| Revit API package pins, year → TFM | `Directory.Build.props` | `docs/ARCHITECTURE.md`, this file's stack section |
| Bundle identity (`ProductCode`, `UpgradeCode`, `AppId`) | `scripts/release.ps1` and `scripts/installer.iss` (a test enforces they match) | `.agents/skills/release-pack/SKILL.md` |

The partner skill lives in `revit-sample-plugin`; its own `AGENTS.md` §13 lists the
partner-side facts.

---

## 📦 Git and Commit Conventions

- Use the Conventional Commits pattern:
  - `feat(connector): ...`
  - `feat(licensing): ...`
  - `fix(ribbon): ...`
  - `docs(readme): ...`
  - `refactor(client): ...`
- Keep commits atomic, focused, and free of temporary build files (`bin/`, `obj/`, `.vs/`).
