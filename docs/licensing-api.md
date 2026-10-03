# Node.aec Licensing API Contract (Wire Protocol)

This document describes the HTTP protocol **consumed by the Node.aec Connector**: endpoints,
payloads, lease claims, the Browser SSO return, and error mapping.

> **Scope**: only what this repository actually calls. The documented fields are
> exactly the ones the client reads — the API may return extra fields without breaking
> compatibility.
>
> **Source of truth** (changed this document, change the code with it):
> `Client/ConnectorApiClient.cs`, `Storage/SigningKeyStore.cs`,
> `Auth/DesktopAuthService.cs`, `Gate/NodeAecGate.cs`, `Models/MasterLeasePayload.cs`.

For the end-user view (installation, windows, messages) see the
[User Manual](USER_MANUAL.md). To protect a partner plugin with the Micro-SDK, see
[Integrating with `NodeAecGate`](../README.md#-integrating-partner-plugins-with-nodeaecgate).

---

## 1. Base URL and configuration

| Environment variable | Default | Use |
|---|---|---|
| `NODEAEC_API_URL` | `https://api.nodeaec.com.br` | Base for all `/license/*` and `/account/*` endpoints |
| `NODEAEC_AUTH_URL` | `https://nodeaec.com.br/auth/desktop` | Login page opened in the browser |
| `NODEAEC_CATALOG_URL` | `https://nodeaec.com.br/products` | Catalog opened by the *Explorar Catálogo* button |
| `NODEAEC_LICENSE_PUBLIC_KEY_SPKI` | *(unset)* | Operations override of the anchor compiled into the add-in. The anchor is the only verification key; the cached JWKS is discovery/diagnostics only. An invalid override fails verification closed |

Fixed production endpoint: **`https://api.nodeaec.com.br`**. There is no private key in this
repository — the client only holds the public key used to **verify** signatures.

---

## 2. Consumed endpoints

| Method | Path | Authentication | Called by |
|---|---|---|---|
| `POST` | `/account/entitlements/lease` | `Bearer <user token>` | Sync (`SyncMasterEntitlementsAsync`) |
| `POST` | `/license/validate` | `Bearer <leaseToken>` | Revit-open heartbeat and *Atualizar minhas licenças* |
| `POST` | `/license/activate` | *(public)* — identifies by `licenseKey` + `machineId` | Manual `NAEC-XXXX-...` key activation |
| `POST` | `/license/deactivate` | *(public)* — identifies by `licenseKey` + `machineId` | Seat release |
| `GET` | `/license/jwks` | *(public)* | Informative JWKS (`kid` discovery and rotation signal; not a trust source) |

Portal endpoints (`/workspace#licenses`) and the web-session-for-desktop-token exchange run
**on the server**, not in the Connector — they are outside this contract.

---

## 3. `POST /account/entitlements/lease`

Fetches the **Master Entitlements Lease**: a single Ed25519 JWT with every grant on the
account, issued for the reported machine.

- **Headers**: `Authorization: Bearer <user token>`, `Content-Type: application/json`
- **Body**:

```json
{
  "machineId": "3b7c89f1a0e4d2...",
  "deviceName": "ESTACAO-PROJETO-01",
  "platform": "Windows / Revit 2026",
  "connectorVersion": "0.1.2"
}
```

`platform` is `ConnectorConfig.PlatformDescription` (the compiled-in Revit year, e.g. `Windows / Revit 2026`) and `connectorVersion` is
`ConnectorConfig.Version`; `machineId` is the SHA-256 hash of the Windows `MachineGuid`
(64 lowercase hex characters). The machine name is **not** part of the hash — it is
renamable and would invalidate the license on every rename. With no readable `MachineGuid` the Connector
fails closed and never calls the API (see [HardwareId.cs](../src/NodeAec.Connector/Hardware/HardwareId.cs)).

- **Response** (fields read by the client):

```json
{
  "success": true,
  "leaseToken": "eyJhbGciOiJFZERTQSI...",
  "expiresAt": "2026-10-20T15:30:00.000Z",
  "grantedCount": 3,
  "totalCount": 3,
  "entitlements": [
    { "slug": "meu-plugin", "name": "Meu Plugin", "licenseKey": "NAEC-A2C4-...",
      "type": "perpetual", "status": "active", "granted": true, "expiresAt": null }
  ]
}
```

**Mandatory post-response sequence**, in this order:

1. `GET /license/jwks` — refreshes the informative key cache (`kid` discovery/rotation; verification uses the compiled-in anchor);
2. writes `leaseToken` to `%APPDATA%\NodeAec\entitlements.lease` via DPAPI;
3. a write failure ⇒ the whole sync fails (*fail-closed*, never plaintext).

---

## 4. `POST /license/validate` (heartbeat)

Renews the lease and updates the machine heartbeat. Called silently when Revit opens
(`App.OnStartup`, on `Task.Run`) and on demand via the *Atualizar minhas licenças* button.

- **Headers**: `Authorization: Bearer <leaseToken>`
- **Body**: `{ "machineId": "3b7c89f1a0e4d2..." }`
- **Response** (fields read by the client):

```json
{
  "valid": true,
  "leaseToken": "eyJhbGciOiJFZERTQSI...",
  "entitlements": [ { "slug": "meu-plugin", "status": "active", "expiresAt": null } ]
}
```

- `valid: false` ⇒ the Connector discards the lease and asks for a fresh login;
- `leaseToken` present ⇒ it is saved with DPAPI (the write may fail ⇒ user-facing error);
- `entitlements` present takes priority over the local lease payload (allows fresher granular
  status, e.g. `seat_released`).

---

## 5. `POST /license/activate`

Activates a manual `NAEC-XXXX-XXXX-XXXX-XXXX` key.

- **Body**:

```json
{
  "licenseKey": "NAEC-A2C4-E6G8-H2K4-M6P8",
  "machineId": "3b7c89f1a0e4d2...",
  "deviceName": "ESTACAO-PROJETO-01",
  "platform": "Windows / Revit 2026",
  "clientVersion": "0.1.2"
}
```

- **Response**: `{ "success": true, "leaseToken": "eyJhbGciOi..." }` (the client only reads
  `leaseToken`).

> ⚠️ **The lease returned here is single-product** (no `entitlements` claim) and **never**
> replaces the local Master Entitlements Lease — writing it would wipe the other grants and the
> gate would start denying everything. That is why activation only takes effect once the account
> resyncs the master lease; the window forces that resync right after the return.

---

## 6. `POST /license/deactivate`

Frees the seat held by this machine.

- **Body**: `{ "licenseKey": "NAEC-A2C4-...", "machineId": "3b7c89f1a0e4d2..." }`
- The client only considers the HTTP success status.

---

## 7. `GET /license/jwks`

Supplies the public keys used in Ed25519 verification (RFC 7517 / RFC 8032).

```json
{
  "keys": [
    { "kty": "OKP", "crv": "Ed25519", "x": "....", "kid": "node-aec-license-1",
      "use": "sig", "alg": "EdDSA" }
  ]
}
```

The client only accepts keys with `kty = OKP`, `crv = Ed25519`, and `x` decoding to 32
bytes. The result is cached at `%APPDATA%\NodeAec\license-jwks.json` (atomic write)
for `kid` discovery and diagnostics only: **signature verification uses
exclusively the anchor compiled into the add-in** (`ConnectorConfig.DefaultLicensePublicKeySpkiBase64`,
overridable via `NODEAEC_LICENSE_PUBLIC_KEY_SPKI`). A JWKS key other than the anchor
is recorded as a rotation signal and **never** verifies a lease. With no usable anchor,
**the gate fails closed**.

---

## 8. Browser SSO return (RFC 8252 loopback)

The Connector opens `https://nodeaec.com.br/auth/desktop?port=<port>&state=<csrf>` in the
default browser and listens on `127.0.0.1`:

| Item | Actual behavior |
|---|---|
| Listener prefix | `http://127.0.0.1:<port>/` (**root** prefix) |
| Accepted path | `/callback` **or** `/callback/` — any other path answers `404` (favicon, probes) |
| Expected query string | `state` (must match the sent one) and `token` (user token) |
| Validation failure | `400` + `Estado inválido ou token ausente.` |
| Timeout | **120 seconds**; on expiry the listener is aborted |
| Transport | Loopback only — nothing travels to an external server on the way back |

There is no PKCE or `code_verifier`: the portal returns the token directly in the local
redirect query string, protected by the anti-CSRF `state`.

---

## 9. Master Entitlements Lease claims

| Claim | Meaning | Validated by the gate |
|---|---|---|
| `iss` | Issuer — must be `node-aec` | ✔ |
| `scope` | Must be `master-lease` (single-product leases are refused) | ✔ |
| `aud` | Audience (string or array) — must include `node-aec-desktop` or `node-aec-plugin` | ✔ |
| `mid` | Issuance machine ID, case-insensitive | ✔ |
| `iat` | Issuance (Unix seconds) — rejected when `iat > now + 5 min` (backdated clock) | ✔ |
| `exp` | End of the offline grace period — the source of the 30-day term | ✔ |
| `entitlements[]` | `slug`, `name`, `licenseKey`, `type`, `status`, `granted`, `expiresAt`, `maxActivations`, `activeActivations` | ✔ (by `slug`) |

**Verification order in `NodeAecGate.Validate(slug)`** — no step advances if the
previous one fails:

1. Ed25519 signature (compiled-in anchor / override) → 2. `iss` → 3. `scope` → 4. `aud` → 5. `iat`
(5-min skew) → 6. `mid` (hardware binding) → 7. `exp` (offline grace) →
8. presence and status of the requested `slug`.

---

## 10. Error codes → displayed message

The API answers `{ "error": true, "status": ..., "type": ..., "code": ..., "message": ... }`.
The Connector **decides on the `code` field**, with this precedence: mapped code → server
`message` → raw code → HTTP status. It never throws (a non-JSON body falls back to the status).

| `code` | Message shown by the Connector |
|---|---|
| `BAD_REQUEST` | Os dados enviados foram recusados. Revise e tente novamente. |
| `UNAUTHORIZED` | Sua sessão expirou. Entre com sua conta novamente. |
| `FORBIDDEN` | Você não tem permissão para esta ação. |
| `NOT_FOUND` | Serviço não encontrado. Verifique a conexão com a plataforma Node.aec. |
| `RATE_LIMITED` / `LICENSE_RATE_LIMITED` | Muitas tentativas em pouco tempo. Aguarde alguns minutos e tente novamente. |
| `ACCOUNT_INACTIVE` | Sua conta não está ativa. Fale com o suporte do Node.aec. |
| `LICENSE_KEY_REQUIRED` | Informe a chave de licença (formato NAEC-XXXX-XXXX-XXXX-XXXX). |
| `MACHINE_ID_REQUIRED` | A identificação da máquina não foi enviada. Reinicie o Connector e tente novamente. |
| `INVALID_LICENSE_KEY_FORMAT` | Formato de chave inválido. A chave deve seguir o formato NAEC-XXXX-XXXX-XXXX-XXXX. |
| `LICENSE_NOT_FOUND` | Chave de licença não encontrada. Verifique a digitação. |
| `LICENSE_EXPIRED` | Esta licença ou período de avaliação expirou. |
| `LICENSE_SUSPENDED` | Esta licença foi suspensa administrativamente. Fale com o suporte do Node.aec. |
| `LICENSE_REVOKED` | Esta licença foi cancelada ou reembolsada. Libere outra chave. |
| `LICENSE_INACTIVE` | Esta licença não está ativa. Fale com o suporte do Node.aec. |
| `TRIAL_ALREADY_USED` | O período de avaliação já foi usado neste computador. Contrate uma assinatura comercial. |
| `ACTIVATION_LIMIT_REACHED` | Limite de assentos simultâneos atingido para esta licença. Desative o assento em outro computador ou pelo portal web. |
| `ACTIVATION_NOT_FOUND` | Este computador ainda não está registrado nesta licença. Ative a chave primeiro. |
| `LEASE_TOKEN_REQUIRED` | Nenhuma licença local encontrada. Clique em atualizar para baixar suas licenças. |
| `INVALID_LEASE_TOKEN` | A licença local é inválida ou foi adulterada. Atualize suas licenças na internet. |
| `LEASE_TOKEN_EXPIRED` | O prazo de tolerância offline expirou. Conecte-se à internet para sincronizar. |
| `MACHINE_MISMATCH` | A licença local pertence a outro computador. Entre com sua conta para ativar este equipamento. |

---

## 11. Offline and isolated workstations

- The **offline grace period** is the API-issued lease's `exp` field (30-day default) —
  there is no client-side environment variable that changes it.
- The whole renewal cycle is *best-effort*: when Revit opens without internet, the heartbeat fails
  silently and the cause is recorded in `%APPDATA%\NodeAec\connector.log`.
- **Air-gapped**: manual key activation needs internet exactly once; after that the
  local lease carries operation for the `exp` term.
- **`.lease` file import** is unavailable in version 0.1 — the
  export/exchange format is not a stable platform contract yet, and a file of unknown
  origin would be refused at signature verification. See the
  [manual alternatives](USER_MANUAL.md#9-importing-a-license-lease-file).
