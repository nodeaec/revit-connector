# Node.aec Connector for Autodesk Revit

The Node.aec Connector is a Revit add-in (Revit 2023–2027) that handles the licenses of every
plugin sold through [Node.aec](https://nodeaec.com.br).

- **End users** sign in once in the browser, and all their Node.aec plugins are unlocked on
  that computer, including up to 30 days offline.
- **Plugin developers** add one line to their commands to check the license. They don't need
  a server, login screens or license keys of their own.

Everything appears on a single **Node.aec** tab in the Revit Ribbon.

---

## Where to start

| I want to… | Read |
|---|---|
| Install and use the Connector | [User Manual](docs/USER_MANUAL.md) |
| Protect **my plugin** with Node.aec licensing | [Partner Integration Guide](docs/PARTNER_INTEGRATION.md) |
| Understand how it works inside (security, flow) | [Architecture](docs/ARCHITECTURE.md) |
| See the server API contract | [Licensing API](docs/licensing-api.md) |
| Change this repository | [Building](#building) below and [AGENTS.md](AGENTS.md) |

---

## Plugin integration in 30 seconds

Add the [`NodeAec.Licensing.Lite`](https://www.nuget.org/packages/NodeAec.Licensing.Lite)
package (`dotnet add package NodeAec.Licensing.Lite`), then at the start of each paid command:

```csharp
using NodeAec.Licensing;

var gate = Gate.Validate("meu-plugin");
if (!gate.IsLicensed)
{
    TaskDialog.Show("Node.aec", gate.Message); // the message already explains why and what to do
    return Result.Cancelled;
}
```

The check runs locally and never touches the network. Your plugin can be
installed as a `.addin` or as a `.bundle`. The full guide covers the Ribbon tab,
packaging and a nicer dialog; the [Sample Plugin](https://github.com/nodeaec/revit-sample-plugin)
is a complete working example.

---

## Building

Requirements: Windows 10/11, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0),
PowerShell, and the Revit year(s) you build for.

```powershell
dotnet build NodeAec.Connector.sln -c Release   # default: Revit 2026
dotnet test  NodeAec.Connector.sln -c Release   # headless, no Revit needed

# Package (.zip + Setup.exe) for one Revit group and install it locally
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 0.2.0 -RevitYear 2025-2026 -Install
```

Groups are `2023-2024`, `2025-2026` and `2027`. Run the script once per group. Details are
in [Architecture → Build matrix](docs/ARCHITECTURE.md#build-matrix-and-packaging).

---

## Contributing

1. Branch from `main`.
2. Follow [AGENTS.md](AGENTS.md).
3. Build with 0 errors and keep all tests green.
4. Open a Pull Request explaining what changed and why.

## Links

[Portal](https://nodeaec.com.br) · [Catalog](https://nodeaec.com.br/products) ·
[Developer area](https://nodeaec.com.br/workspace) ·
[Issues](https://github.com/nodeaec/revit-connector/issues)
