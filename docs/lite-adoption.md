# The Connector uses NodeAec.Licensing.Lite

The Connector does not have its own copy of the license check. It references the
[`NodeAec.Licensing.Lite`](https://www.nuget.org/packages/NodeAec.Licensing.Lite) package, the
same one partner plugins use, for two things:

| What | Lite API | Where in the Connector |
|---|---|---|
| Verify a lease's Ed25519 signature before saving it | `LeaseSignatureVerifier.Evaluate` | `Cryptography/LeaseTrust.cs` → `ConnectorApiClient.VerifyLeaseBeforeSave` |
| Compute the Machine ID (`mid`) sent to the API and checked in the lease | `HardwareId.TryGetMachineId` | `Cryptography/LeaseTrust.cs` → `ConnectorApiClient`, `ConnectorWindow` |
| Warn when the JWKS publishes a key Lite does not trust (rotation) | `LeaseSignatureVerifier.IsTrustedKey` | `LeaseTrust.SignalJwksOutsideAnchors` |

Why: when the Hub and the plugins ran different copies, a change in one (a new anchor, a
different `mid` rule) could make the Connector save a lease that every plugin then rejected.
With one implementation, a lease the Connector accepts is a lease the plugin gates accept.

The Connector never calls `Gate.Validate`. It writes the lease; the plugins decide.

## Rules

- **No second source of trust.** This repository compiles no anchor and has no environment
  variable that replaces one. `ConnectorConfigTests.AddIn_CompilesNoTrustAnchor` fails if an
  Ed25519 SPKI key appears under `src/`.
- **Tests swap delegates, not keys.** Lite's anchors are private and cannot be changed from
  outside. `LeaseTrust.VerifySignature`, `MachineIdReader` and `IsTrustedKey` are `internal`
  and only the test project assigns them (`TestHelpers.WithTestLicensePin`, `WithMachineGuid`).
- **Ship the newest Lite.** Every Lite release is followed by a Connector release that
  references it. See *Versions in one Revit process* below.

## Versions in one Revit process

The Connector and each partner plugin carry their own copy of `NodeAec.Licensing.Lite.dll`.
What happens when they differ depends on the runtime:

- **Revit 2023/2024 (.NET Framework 4.8):** the package is strong-named and each assembly
  version is distinct, so different versions load side by side.
- **Revit 2025+ (.NET 8/10):** only one version per assembly name loads in the default
  context. The first one loaded wins. A later request for a lower or equal version gets it;
  a request for a **higher** version fails with `FileLoadException`.

The Connector loads Lite early (the background sync at startup uses it). So it must always
ship the newest Lite: a plugin built against an older or equal version keeps working, and a
plugin built against a newer version than the installed Connector fails on Revit 2025+.
Lite stays additive-only (no member is removed or changed) so that "older plugin, newer Lite"
always works.

## Release order for a Lite change

1. Release Lite (`revit-licensing-lite/scripts/release.ps1`) and push it to nuget.org.
2. Bump `NodeAec.Licensing.Lite` in `src/NodeAec.Connector/NodeAec.Connector.csproj` and in the
   test project, build all three Revit groups, run the tests, release the Connector.
3. Only then tell partners about the new Lite version.
