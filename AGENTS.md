# AGENTS.md — Engineering and Governance Guidelines for AI Agents

Canonical engineering guide for autonomous AI agents (Antigravity, Claude Code, Cursor, OpenCode, Copilot) that develop, maintain, or refactor code inside the **`nodeaec/revit-connector`** repository.

Official repository: [github.com/nodeaec/revit-connector](https://github.com/nodeaec/revit-connector)

---

## 🎯 Repository Scope and Mission

The `revit-connector` repository maintains the **Node.aec Connector** — the official desktop governance, licensing, and unified-Ribbon add-in from **Node.aec** for Autodesk Revit — plus the public contracts for partner-plugin integration via `NodeAecGate`.
The goal is to accelerate the AEC/BIM developer ecosystem by standardizing integration with the Node.aec platform (licensing, catalog, updates) and serving as an engineering reference for professional plugins.

### Primary Project in the Repository

1. **`NodeAec.Connector`**:
   - The **central desktop governance Hub** and unified Ribbon for Node.aec in Autodesk Revit.
   - Manages browser SSO authentication (RFC 8252 loopback), master entitlements lease sync (`entitlements.lease`), and the user interface for manual NAEC key activation.
   - Provides the `NodeAecGate` micro-SDK (`NodeAecGate.Validate(slug)`), letting third-party plugins validate rights in `< 1ms` securely, locally, and with no blocking network calls.
   - Manages the canonical **`Node.aec`** tab and deduplication via `Autodesk.Windows.ComponentManager`.

> [!NOTE]
> If your goal is to explain how to integrate Node.aec licensing into an **external user plugin**, see the [`.agents/skills/licensing-integrate`](.agents/skills/licensing-integrate/SKILL.md) skill and the [README integration section](README.md#-integrating-partner-plugins-with-nodeaecgate). This file governs **internal development of this repository**.

---

## 🧩 Modular Skills (.agents/skills/)

This repository ships specialized modular skills for autonomous AI agents. Invoke the skill matching the task goal:

### 1. Revit Domain & Tooling
| Skill | Scope and Activation Triggers | Canonical Path |
|---|---|---|
| **`licensing-integrate`** | Integrate Node.aec licensing into new or existing plugins, run the Grilling phase, protect commercial commands (`IExternalCommand`). | [`.agents/skills/licensing-integrate`](.agents/skills/licensing-integrate/SKILL.md) |
| **`ribbon-guard`** | Create/modify Ribbon panels and buttons, enforce the `Node.aec` tab, non-blocking icons, and deduplication via AdWindows. | [`.agents/skills/ribbon-guard`](.agents/skills/ribbon-guard/SKILL.md) |
| **`revit-build-validate`** | Build via `dotnet build`, enforce 0 errors, isolate RevitAPI DLLs, and validate runtime dependencies. | [`.agents/skills/revit-build-validate`](.agents/skills/revit-build-validate/SKILL.md) |
| **`release-pack`** | Package `.zip` releases and the single-Revit-year-group installer via `release.ps1`, verify SHA-256, and install into local Revit (`%ProgramData%`). | [`.agents/skills/release-pack`](.agents/skills/release-pack/SKILL.md) |

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

---

## 🏗️ Technology Stack and Runtimes

- **Language**: C# 12
- **Target Framework**: per-Revit-year matrix — `net48` (2023/2024), `net8.0-windows` (2025/2026), and `net10.0-windows` (2027)
- **Host Application**: Autodesk Revit 2023–2027 (per-year build; per-compatibility-group installer)
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
- **Key Security**: NEVER store private keys in client code. The repository only handles the compiled-in SPKI public key (`ConnectorConfig.DefaultLicensePublicKeySpkiBase64`) — the only anchor accepted when verifying Ed25519 signatures.
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

# Package and install into local Revit 2026 via the 2025-2026 group (repeat with -RevitYear 2023-2024 and -RevitYear 2027 for the other groups)
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 0.1.2 -RevitYear 2025-2026 -Install
```

> [!WARNING]
> **DPAPI coverage is manual now.** `ProtectedData.Protect/Unprotect(..., DataProtectionScope.CurrentUser)` only works in a real logon session (SessionId ≥ 1). In an SSH/`services.exe` context the process runs in Session 0 with no Logon SID (`S-1-5-5-*`) and no `AuthenticationId`, and Windows returns `win32 = 5 Access denied` — the behavior is *fail-closed* by design. The unit tests that depended on that path were removed, so `dotnet test` must come back **100% green in any session (SSH or interactive)**. DPAPI persistence (writing/reading `entitlements.lease` and `session.json` under `%APPDATA%`) is now validated manually in the in-Revit add-in smoke test. Do not weaken storage or mark tests as Skip to compensate.

Acceptance Criteria for Changes:
- Clean build: **0 Errors**.
- Unit tests: 100% passing.
- No Revit assemblies (`RevitAPI*.dll`) inside `release/` or `stage/`.
- `System.Security.Cryptography.ProtectedData.dll` must be present in the final add-in payload **only when targeting `net48`** (Revit 2023/2024). On `net8.0-windows`/`net10.0-windows` it is provided by `Microsoft.WindowsDesktop.App` and must not appear in `release/` or `stage/`.

---

## 📦 Git and Commit Conventions

- Use the Conventional Commits pattern:
  - `feat(connector): ...`
  - `feat(licensing): ...`
  - `fix(ribbon): ...`
  - `docs(readme): ...`
  - `refactor(client): ...`
- Keep commits atomic, focused, and free of temporary build files (`bin/`, `obj/`, `.vs/`).
