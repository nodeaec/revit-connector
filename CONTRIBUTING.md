# Contributing to the Node.aec Connector for Autodesk Revit

Thank you for your interest in contributing to the **Node.aec Connector**! This repository houses the central desktop governance, licensing client, and unified Revit Ribbon hub for the Node.aec ecosystem across Autodesk Revit 2023–2027.

To ensure consistency, stability, and zero runtime errors across thousands of architectural and engineering workstations, all contributions must adhere to the engineering guidelines, Git workflows, and commit conventions outlined below.

---

## 🧭 Repository Engineering Principles

1. **Canonical `Node.aec` Tab**:
   - Every tool, window, and command produced in this repository resides exclusively on the **`Node.aec`** ribbon tab (`TabName = "Node.aec"`).
   - Fragmented individual tabs are prohibited.
   - Panel deduplication and ordering are handled via `AdWindows` (`Autodesk.Windows.ComponentManager`).
2. **Revit API Binary Isolation**:
   - Never copy Revit host assemblies (`RevitAPI.dll`, `RevitAPIUI.dll`, `AdWindows.dll`). All references carry `PrivateAssets="all"` and `ExcludeAssets="runtime"`.
   - `System.Security.Cryptography.ProtectedData.dll` is an add-in payload **only for `net48`** (Revit 2023/2024); on modern targets (`net8.0-windows` and `net10.0-windows`), it is runtime-provided by `Microsoft.WindowsDesktop.App`.
3. **Fail-Closed Security & Trust**:
   - Cryptographic lease verification (Ed25519) and machine fingerprinting are executed via the `NodeAec.Licensing.Lite` package.
   - Client code never contains private keys.
   - Network failures or offline transitions fail closed safely without crashing Revit.
4. **UI Thread Safety (Non-blocking)**:
   - Network requests, file I/O, and token refreshes must run asynchronously off Revit's main UI thread.

---

## 🌿 Git Workflow & Delivery Tiers

Before making changes, determine the delivery tier according to the scope of your work:

| Delivery Tier | Scope | Git Policy |
|---|---|---|
| **Fast Track** | Minor typo fixes, documentation revisions, isolated constant edits, or comment updates. | Can be committed directly to your working branch or PR branch with atomic commits. |
| **Planned Track** | New add-in features, UI modifications, Ribbon alterations, build/release script changes, or architectural refactoring. | Create a dedicated branch from `main`: `feat/<slug>` or `fix/<slug>`. Commit verified units atomically. |

### Branch Naming Conventions
- Features & enhancements: `feat/<short-description>` (e.g., `feat/ribbon-tint`)
- Bug fixes: `fix/<short-description>` (e.g., `fix/tab-dedup`)
- Documentation: `docs/<short-description>` (e.g., `docs/contributing-guide`)
- Maintenance / Dependencies: `chore/<short-description>` (e.g., `chore/bump-lite`)

---

## 📝 Commit Conventions (Semantic Commits)

This repository enforces standardized **Semantic Commits**. Every commit must be short, imperative, atomic, and specific to the staged changes.

### Format
```text
<type>(<scope>): <short summary>
```

- **Lowercase**: Both `<type>` and `<scope>` must be strictly lowercase.
- **Imperative Mood**: Summary must start with an imperative verb (e.g. `add`, `fix`, `update`, `refactor`, `remove`).
- **No Scope Omission**: `<scope>` is mandatory.
- **Length**: Keep the entire subject line at 72 characters or fewer.
- **No Emojis**: Emojis are not permitted in commit titles.
- **Atomic Commits**: Stage and commit cohesive, logically isolated units.

### Permitted Types

| Type | When to Use |
|---|---|
| `feat` | Adds user-facing features, UI commands, workflows, or capabilities. |
| `fix` | Corrects broken behavior, crashes, incorrect state, UI defects, or security issues. |
| `refactor` | Restructures code without changing observable behavior or adding features. |
| `test` | Adds or updates unit tests, test fixtures, or test harnesses. |
| `docs` | Modifies documentation, manuals, architectural guides, comments, or examples. |
| `style` | Code formatting, whitespace, spelling, or lint-only fixes without logic change. |
| `chore` | Build system, CI/CD, dependency updates, packaging scripts, or repo maintenance. |

### Permitted Scopes for Revit Add-ins

| Scope | Area of Responsibility |
|---|---|
| `licensing` | License client, cryptographic verification, DPAPI, lease sync, machine ID, activation. |
| `ribbon` | Revit Ribbon tabs, panels, push buttons, icons, ordering, and AdWindows deduplication. |
| `client` | HTTP API communication (`ConnectorApiClient`), JSON payloads, catalog resolution. |
| `ui` | WPF windows, dialogs, styling, view models, and `UiTheme` color tokens. |
| `commands` | `IExternalCommand` implementations and commercial gating. |
| `config` | `ConnectorConfig`, endpoints, environment settings, and version constants. |
| `release` | Packaging scripts (`release.ps1`), `.addin` manifests, installer (`installer.iss`), staging. |
| `deps` | NuGet packages, assembly references, and .NET target framework matrices. |
| `docs` | README files, agent guides, manuals, and architectural documentation. |
| `repo` | Repository-wide maintenance or changes crossing multiple functional scopes. |

### Commit Examples
```text
feat(ribbon): add subtle brand background tint to nodeaec panel title
fix(ribbon): deduplicate node.aec tab on add-in reload
refactor(client): simplify ed25519 lease token signature verification
docs(contributing): add developer contribution and commit guidelines
chore(release): package protecteddata dll in net48 stage output
```

### Staging Rules
- **Explicit staging only**: Use `git add <file1> <file2>`.
- **Forbidden**: Do not use `git add .` or `git add -A`, to prevent accidental check-ins of build artifacts (`bin/`, `obj/`, `release/`, `.vs/`).

---

## 🛠️ Build & Verification

Before submitting any commit or opening a Pull Request, verify that your changes compile cleanly and pass all validation gates.

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10/11 (or Linux for headless compilation and tests)
- Autodesk Revit 2023–2027 (for local in-app smoke testing)

### Verification Commands
```powershell
# 1. Compile the solution (Release configuration, defaults to Revit 2026)
dotnet build NodeAec.Connector.sln -c Release

# 2. Run unit tests
dotnet test NodeAec.Connector.sln -c Release

# 3. Compile across the Revit target matrix (2023–2026)
dotnet build src/NodeAec.Connector/NodeAec.Connector.csproj -c Release -p:RevitYear=2023
dotnet build src/NodeAec.Connector/NodeAec.Connector.csproj -c Release -p:RevitYear=2024
dotnet build src/NodeAec.Connector/NodeAec.Connector.csproj -c Release -p:RevitYear=2025
dotnet build src/NodeAec.Connector/NodeAec.Connector.csproj -c Release -p:RevitYear=2026

# 4. Package bundle zip and Setup.exe (Windows PowerShell)
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 0.4.0
```

> [!IMPORTANT]
> **Zero Warnings & Zero Errors**: The build must pass with **0 Errors** and **0 Warnings**.

> [!WARNING]
> **DPAPI Testing Note**: Windows DPAPI (`DataProtectionScope.CurrentUser`) requires an interactive logon session (SessionId ≥ 1). When running headless tests in Session 0 (such as automated SSH or CI containers), Windows DPAPI returns `Access Denied` by design. Pure logic tests in `NodeAec.Connector.Tests` run headlessly; DPAPI persistence is validated in real Revit sessions.

---

## 📐 Coding & Documentation Standards

- **C# 12 & .NET 8**: Follow clean code practices: SOLID principles, early returns, guard clauses, and concise single-responsibility methods.
- **Null Safety**: `<Nullable>enable</Nullable>` is strictly enforced. Handle null references safely; avoid suppressing warnings with `!`.
- **XML Documentation**: Every touched public or internal class, interface, method, and property must include standard C# XML doc comments (`/// <summary>`, `<param>`, `<returns>`).
- **Headless Testability**: Decouple domain decisions (e.g., [`RibbonDecisions`](src/NodeAec.Connector/UI/RibbonDecisions.cs), [`UiState`](src/NodeAec.Connector/UI/UiState.cs)) from WPF/Revit types so they can run isolated in xUnit test suites.

---

## 🚀 Pull Request Checklist

Before opening your Pull Request, ensure:

- [ ] A dedicated branch was created from `main` (`feat/*`, `fix/*`, `chore/*`, `docs/*`).
- [ ] Code builds cleanly with **0 errors** and **0 warnings** on all target years.
- [ ] Unit tests are 100% passing.
- [ ] Commits strictly follow `<type>(<scope>): <summary>`.
- [ ] No `RevitAPI*.dll` or `AdWindows.dll` binaries appear in `bin/` or `release/`.
- [ ] Public and internal APIs have comprehensive XML documentation.
- [ ] The PR description clearly details the motivation, changes made, and how they were tested.
