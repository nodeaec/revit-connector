# Lite Core Adoption by the Connector (wave C1)

> Status on 2026-10-02: **delegation NOT active**. Adopted path: this document + the
> parity test `tests/NodeAec.Connector.Tests/NodeAecGateVsLiteTests.cs`, **without
> changing** `NodeAecGate`, `ConnectorConfig`, `App.cs`, endpoints, or messages.

## 1. Decision and rationale

Enabling delegation now would be premature for four reasons verified in code:

1. **Lite namespace in flux.** Another agent is normalizing
   `NodeAec.Licensing.Lite` to `NodeAec.Licensing`. Any `ProjectReference`
   + `using` added now breaks on the rename. This repo does not depend on the final
   namespace until it freezes (wave 0, `CONTRACT.md`).
2. **A conditional `ProjectReference` would be phantom delegation.** With a relative
   path `..\..\..\revit-licensing-lite\...` and `Choose/When` on file existence, CI (which
   clones each repo in isolation) would always take the fallback — the
   "delegate" would never run where it matters most. A local reference only makes sense
   as a `PackageReference` to the Lite published on NuGet.
3. **No STJ/BouncyCastle conflict on net48, but no gain.** Verified: the
   Connector gets `System.Text.Json 8.0.5` via `Directory.Build.props:79-80` and Lite
   via its own csproj (same version); `BouncyCastle.Cryptography 2.7.0` matches
   on both sides. NuGet would deduplicate — the barrier is not technical; it is items
   1, 2, and 4.
4. **Intentional Hub-vs-Lite behavioral divergence.** Naive delegation
   (swapping the `Validate` body for a Lite call) would regress the Hub:
   - the operations override `NODEAEC_LICENSE_PUBLIC_KEY_SPKI`
     (`ConnectorConfig.cs:91-92`, `LeaseSignatureVerifier.cs:153-167`) — Lite is
     anchor-only by design (plan §7) and ignores the variable;
   - `WARN`/`ERROR` logs via `ConnectorLog` (rejected signature, unavailable
     Machine ID, unexpected error) — Lite is silent;
   - the JWKS rotation warning (`SignalJwksOutsideAnchor`, once per process).
   The plan says to keep the override "Hub-only, with a `WARN` log". The correct swap
   preserves that branch (section 4).

## 2. Verified state (parity basis)

Against Lite `1.0.0-preview.1` (`src/NodeAec.Licensing.Lite/Gate.cs` and
`Cryptography/LeaseSignatureVerifier.cs`), verified by inspection on 2026-10-02:

- **Identical anchor:** `ConnectorConfig.DefaultLicensePublicKeySpkiBase64`
  (`Config/ConnectorConfig.cs:81-82`) == Lite's `TrustedAnchors[0]`.
- **Identical verification order** (same sequence, same conditions):

| # | Step | Connector (`Gate/NodeAecGate.cs`) | Lite (`Gate.cs`) |
|---|-------|-----------------------------------|------------------|
| 0 | empty slug | 67-70 | 31-34 |
| 1 | missing lease | 72-76 | 36-40 |
| 2 | unreadable payload | 80-84 | 44-48 |
| 3 | Ed25519 signature | 89-93 | 53-56 |
| 4 | `iss == "node-aec"` | 96-99 | 59-62 |
| 5 | `scope == "master-lease"` | 101-104 | 64-67 |
| 6 | platform audience | 108-111 | 71-74 |
| 7 | `iat` more than 300s in the future | 115-118 | 78-81 |
| 8 | Machine ID | 123-131 | 85-93 |
| 9 | `exp` / `IsExpired` | 136-141 | 98-103 |
| 10 | product slug | 144-150 | 106-112 |
| 11 | `IsActive()` + `seat_limit_reached` / expired / status branches | 152-165 | 114-127 |
| 12 | success | 167 | 129 |
| 13 | catch-all | 169-173 | 131-134 |

- **Byte-identical PT-BR taxonomy:** 17 failure messages + 1 success message
  (`"Licença ativa e verificada."`), including the three interpolated ones
  (`{exp:dd/MM/yyyy}`, `'{productSlug}'`, `'{item.Name}'`/`'{item.Status}'`).
  Literal comparison done via `grep` on both files (18 literals, same order).
- **Semantically equal models:** `EntitlementItem` (only namespace/comments
  differ) and `MasterLeasePayload` with the same `exp`/`iat` plausibility and the same
  fail-closed `IsExpired`; `HardwareId` with the same injectable `MachineGuidReader`.
- **Preserved compatible contract:** `public static class NodeAecGate` +
  `Validate(string)` + `GateResult` (`Success`/`Failure` factories, same fields)
  + silent no-op `OpenConnector()` via reflection — signatures and messages
  unchanged.
- What the parity test covers today lives in
  `NodeAecGateVsLiteTests.cs` (DPAPI-free per the `AGENTS.md` rule); what was
  deferred is in section 5.

## 3. Intentional divergences to preserve in the swap

| Behavior | Connector (Hub) | Lite | Delegation action |
|---|---|---|---|
| `NODEAEC_LICENSE_PUBLIC_KEY_SPKI` | valid/invalid override (fails closed) | ignored | keep the Hub branch: with the override set, use the current local path; without it, delegate |
| `ConnectorLog` WARN/ERROR | yes (3 points in the Gate + JWKS rotation) | no | keep logs in the Hub wrapper |
| JWKS rotation warning | `SignalJwksOutsideAnchor` | does not exist | keep in the Hub |
| `TryVerify(jwt, out reason)` / `Evaluate` | granularity used by `ConnectorApiClient` (M1) | Lite's own API | do not remove; only the `Validate` body delegates |

## 4. Swap plan (for the real-delegation agent)

Prerequisites (do not start before): frozen Lite namespace + approved `CONTRACT.md`
(wave 0) + Lite 1.x published on NuGet.org with `.snk`/Authenticode.

1. Add a `PackageReference` to Lite (pinned version `x.y.*`) in
   `NodeAec.Connector.csproj`. Never a relative `ProjectReference` across repos.
2. Rewrite **only the body** of `NodeAecGate.Validate` as a wrapper:
   - keep the class, `GateResult`, `Success`/`Failure`, `OpenConnector()`, and all
     18 byte-identical messages (map `Snapshot`→`GateResult` field by field:
     `IsLicensed`, `LicenseType`, `LicenseKey`, `ProductName`, `ExpiresAt`,
     `Message` verbatim);
   - keep the override branch: `LicensePublicKeySpkiOverride` set →
     current local path (operations behavior); unset → delegate to Lite;
   - keep the Gate's 3 `ConnectorLog.Write` calls and the `SignalJwksOutsideAnchor`;
   - keep the `AssemblyResolve` in `App.cs:32-55` and the 6h `HeartbeatPeriod`.
3. Extend `NodeAecGateVsLiteTests.cs`: with the package referenced, add the
   full matrix with the lease installed via `SaveMasterLease` (requires an
   interactive DPAPI session; see section 5), comparing `IsLicensed` + `Message` of
   both gates case by case.
4. Validate: `dotnet build NodeAec.Connector.sln -c Release -p:RevitYear=2026`
   (repeat 2023/2027 across the matrix), `dotnet test -c Release` 100%, `release/stage`
   with no `RevitAPI*`/`AdWindows`, `ProtectedData.dll` only on `net48`.
5. Forbidden: changing endpoints (`https://api.nodeaec.com.br`), logging secrets
   (variable names + exception types only), changing any PT-BR message,
   accepting `NODEAEC_LICENSE_PUBLIC_KEY_SPKI` on the Lite-delegated path.

## 5. Deferred parity matrix (requires installed lease / DPAPI)

Cases covered by inspection (section 2), to convert into direct tests at delegation time.
Sign each JWT with the test key (RFC 8032 seed) and pin via
`WithTestLicensePin()` + `WithMachineGuid(...)`; install with `SaveMasterLease`:

1. Invalid signature → `"A licença local não passou na verificação de segurança. ..."`
2. Foreign `iss` → `"Origem da licença local desconhecida. ..."`
3. Foreign `scope` → `"A licença local está em formato não suportado. ..."`
4. Missing/foreign `aud` → `"A licença local não foi emitida para este add-in. ..."`
5. `iat` more than 300s in the future → `"A data da licença local é inválida. ..."`
6. Divergent `mid` → `"A concessão de licenças foi emitida para outra estação ..."`
7. Past `exp` → `"O prazo de tolerância offline expirou em {dd/MM/yyyy}. ..."`
8. Implausible `exp` → `"O prazo da licença local não pôde ser lido. ..."`
9. Missing slug → `"O produto '{slug}' não consta nas licenças ativas ..."`
10. `seat_limit_reached` → `"O limite de computadores simultâneos para '{name}' foi atingido."`
11. Expired item → `"A licença ou período de teste de '{name}' expirou em {dd/MM/yyyy}."`
12. Generic status → `"A licença de '{name}' está com status '{status}'."`
13. Valid → success (`IsLicensed`, `LicenseType`, `LicenseKey`, `ProductName`, `ExpiresAt`)
14. Corrupt payload → `"Concessão corrompida ou estrutura inválida. ..."`
15. Internal exception → `"Não foi possível verificar a licença local. ..."`

## 6. Payload rules (AGENTS.md, wave-C1 verification)

- `RevitAPI*.dll`, `RevitAPIUI.dll`, `AdWindows.dll`: `<Private>false</Private>`,
  never in `release/` or `stage/`.
- `System.Security.Cryptography.ProtectedData.dll`: present in the payload **only**
  when targeting `net48` (Revit 2023/2024); on `net8.0-windows`/`net10.0-windows`
  it comes from `Microsoft.WindowsDesktop.App` and is not copied.
