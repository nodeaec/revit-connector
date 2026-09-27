# Node.aec Connector — Autodesk Revit Add-in

Add-in central de governança desktop, gerenciamento de licenças e Ribbon unificada para **Autodesk Revit 2023–2027**, com um instalador independente por ano (matriz de compilação).

O **Node.aec Connector** atua como o Hub no modelo **Hub & Micro-Gate**: o usuário final realiza login uma única vez no navegador (Browser SSO com loopback local RFC 8252) e tem todos os seus plugins, templates e famílias licenciados e sincronizados automaticamente na estação de trabalho com tolerância de até 30 dias offline.

Repositório oficial: [github.com/nodeaec/revit-connector](https://github.com/nodeaec/revit-connector) · Issues: use o [issue tracker](https://github.com/nodeaec/revit-connector/issues) para relatar problemas com passo a passo.

📖 **Documentação**: [Manual do Usuário](docs/USER_MANUAL.md) · [Contrato da API de Licenciamento](docs/licensing-api.md)

---

## 🚀 Principais Recursos

- **Aba Canônica `Node.aec`**: Registra e gerencia o painel oficial `Conector` na Ribbon do Revit com botões de acesso rápido e deduplicação automática de abas via `AdWindows`.
- **Browser SSO (OAuth 2.0 Loopback Local — RFC 8252)**: Autenticação moderna e segura com suporte a login com Google e 2FA sem digitação de senhas no Revit.
- **Master Entitlements Lease**: Obtém e renova concessões consolidadas de múltiplos produtos, com verificação Ed25519 (RFC 8032) da assinatura **antes** de confiar em qualquer claim.
- **Âncora de assinatura compilada (pin)**: A chave pública Ed25519 de produção é compilada no add-in (`ConnectorConfig.DefaultLicensePublicKeySpkiBase64`) e é a **única** chave aceita para verificar leases. O JWKS (`GET /license/jwks`, cacheado em `%APPDATA%\NodeAec\license-jwks.json`) serve para descoberta de `kid` e sinal de rotação, nunca como fonte de confiança. A operação pode sobrepor a âncora via `NODEAEC_LICENSE_PUBLIC_KEY_SPKI`; sem âncora utilizável o gate falha fechado.
- **Armazenamento Seguro DPAPI**: O arquivo `%APPDATA%\NodeAec\entitlements.lease` é criptografado com `DataProtectionScope.CurrentUser`; falha de DPAPI em Windows não degrada para texto puro.
- **Modo Offline & Air-Gapped**: Entrada manual de chaves (`NAEC-XXXX-...`). A importação de arquivos `.lease` está **adiada para uma iteração futura** e o link correspondente foi **removido da UI** (o formato de exportação/troca ainda não é um contrato estável).
- **Micro-SDK `NodeAecGate`**: Classe canônica para plugins parceiros validarem permissão de execução localmente — sem requisições de rede no caminho crítico, em poucos milissegundos.
- **Diagnóstico Local**: Erros de API mapeados para códigos estáveis e log sanitizado em `%APPDATA%\NodeAec\connector.log` (rotação de 512 KB, sem tokens).

---

## 🏛️ Arquitetura: Hub & Micro-Gate

Em vez de cada plugin parceiro implementar um cliente HTTP próprio, apresentar telas de
ativação, solicitar chaves individuais (`NAEC-XXXX-...`) e gerenciar criptografia de máquina,
o Node.aec concentra tudo num **Hub** e entrega aos plugins um **Micro-Gate** local:

```
+--------------------------------------------------------------------------+
|                              Autodesk Revit                              |
|                                                                          |
|  [ Aba "Node.aec" ]                                                      |
|                                                                          |
|  +---------------------------+      +----------------------------------+ |
|  |   Node.aec Connector      |      |      Plugins Parceiros           | |
|  |      (Hub central)        |      |   (Revit Automator, Portas, ...) | |
|  |                           |      |                                  | |
|  |  - Browser SSO (loopback) |      |    public Result Execute(...)    | |
|  |  - Master Entitlements    |      |    {                             | |
|  |    Lease + heartbeat      |      |      var r = NodeAecGate         | |
|  |  - Armazenamento DPAPI    |      |              .Validate(slug);    | |
|  |  - JWKS / Ed25519         |      |      if (!r.IsLicensed)          | |
|  |  - Dedup. de abas         |      |          return Result.Cancelled;| |
|  +---------------------------+      |      }                           | |
|                                     +----------------------------------+ |
|   ^                                 | leitura local do plugin, < 1 ms    |
|   | lease assinado, em DPAPI        | sem nenhuma chamada de rede        |
|   | em %APPDATA%\NodeAec\           |                                    |
|   | entitlements.lease              |                                    |
|                                                                          |
|   ^                                 |                                    |
|   | HTTPS                           | abre o navegador padrão            |
|   v                                 v                                    |
|   https://api.nodeaec.com.br        https://nodeaec.com.br               |
+--------------------------------------------------------------------------+
```

**O que o Hub absorve para o plugin parceiro**

1. **Nenhuma infraestrutura de rede no plugin** — o parceiro não escreve um cliente HTTP nem
   conhece endpoints, tokens de sessão ou formatos de resposta.
2. **Nenhuma UI de ativação própria** — login, chave manual e gestão de assentos vivem nas
   janelas *Minha Conta* e *Meus Plugins*.
3. **Nenhuma gestão de criptografia** — o Hub grava o lease com DPAPI `CurrentUser` e falha
   fechado; o plugin só lê.
4. **Uma única autenticação** — o usuário entra uma vez e todos os produtos da conta são
   sincronizados juntos.
5. **Validação local em < 1 ms** — `NodeAecGate` é puro CPU, sem I/O de rede no caminho
   crítico, com tolerância de 30 dias offline.
6. **Ribbon unificada** — tudo acontece na aba canônica `Node.aec`, sem abas fragmentadas.

> Contrato HTTP consumido pelo Hub (endpoints, payloads, claims e códigos de erro):
> [docs/licensing-api.md](docs/licensing-api.md).

---

## 🔄 Fluxo de Licença e Heartbeat

| # | Quando | O que acontece |
|---|---|---|
| 1 | Usuário clica em **Entrar com minha conta** | O Connector escolhe uma porta efêmera livre, escuta em `127.0.0.1` e abre `https://nodeaec.com.br/auth/desktop?port=…&state=…` no navegador padrão (120 s de timeout, `state` anti-CSRF). |
| 2 | Login concluído no navegador | O portal redireciona para `http://127.0.0.1:<porta>/callback?token=…&state=…`; o listener valida o `state` e devolve a página *Login Concluído*. |
| 3 | Janela dispara a sincronização | `POST /account/entitlements/lease` devolve o **Master Entitlements Lease** assinado em Ed25519. |
| 4 | Resposta recebida | `GET /license/jwks` atualiza o cache informativo de chaves públicas (a verificação usa a âncora compilada, não o cache), depois o lease é gravado em `%APPDATA%\NodeAec\entitlements.lease` com DPAPI. Gravação falhou ⇒ erro ao usuário, sem texto puro. |
| 5 | Abertura do Revit (sempre) | Heartbeat em `Task.Run`: `POST /license/validate` renova o lease; falha de rede é silenciosa e fica em `connector.log`. |
| 6 | Plugin parceiro executa | `NodeAecGate.Validate(slug)` verifica assinatura → `iss` → `scope` → `iat` → `mid` → `exp` → `slug` e libera ou bloqueia — **sem rede**. |
| 7 | Sem internet | O lease local vale até o `exp` emitido pela API (padrão de 30 dias); a cada abertura do Revit a validade é tentativamente estendida. |

---

## 📁 Estrutura do Repositório

```text
revit-connector/
├── AGENTS.md                          # Diretrizes e regras para agentes de IA neste repositório
├── Directory.Build.props              # Matriz de compilação por ano do Revit (2023–2027)
├── README.md                          # Este documento (documentação completa do Connector)
├── NodeAec.Connector.sln              # Solution (matriz .NET/Revit 2023–2027)
├── docs/                              # Manual do usuário e contrato da API de licenciamento
├── scripts/
│   └── release.ps1                    # Script de compilação, empacotamento e deploy local
├── src/NodeAec.Connector/
│   ├── Auth/                          # DesktopAuthService (Browser SSO Loopback RFC 8252)
│   ├── Client/                        # ConnectorApiClient (Master Entitlements Lease)
│   ├── Gate/                          # NodeAecGate (Micro-SDK de validação local < 1ms)
│   ├── Storage/                       # LeaseStorage (Persistência DPAPI %APPDATA%\NodeAec)
│   ├── UI/                            # ConnectorWindow (Interface WPF moderna)
│   └── App.cs                         # IExternalApplication (Ribbon Node.aec e deduplicação)
└── tests/NodeAec.Connector.Tests/     # Testes unitários (net8.0, CI-safe)
```

---

## 🛠️ Ambiente e Pré-requisitos

Para compilar e contribuir com o add-in:

- **Sistema Operacional**: Windows 10 ou 11 (64-bit)
- **Autodesk Revit**: 2023 a 2027 instalado no caminho padrão (`C:\Program Files\Autodesk\Revit <ano>`) — compilação e empacotamento são feitos um ano por vez (`-RevitYear`)
- **SDK .NET**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Shell**: PowerShell 5.1 ou PowerShell 7+

---

## 💻 Como Compilar e Testar

```powershell
# Compilar a solution
dotnet build NodeAec.Connector.sln -c Release

# Executar os testes unitários (headless, sem Revit)
dotnet test tests\NodeAec.Connector.Tests\NodeAec.Connector.Tests.csproj

# Empacotar o .zip e o instalador de UM ano do Revit e instalar localmente
powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 0.1.2 -RevitYear 2026 -Install
```

Cada execução do script gera artefatos de **um único ano** do Revit (`-RevitYear`, padrão `2026`): o `.zip` `release/NodeAec.Connector-<versão>-R<ano>.zip` e, quando o [Inno Setup 6](https://jrsoftware.org/isdl.php) está instalado, o instalador `release/NodeAec.Connector-<versão>-R<ano>-Setup.exe`. Cada instalador atende apenas `%ProgramData%\Autodesk\Revit\Addins\<ano>\` e pode ser desinstalado de forma independente em Aplicativos. Repita com `-RevitYear 2023`..`2027` para gerar os cinco instaladores; sem o Inno Setup, apenas o `.zip` é produzido.

---

## 🔌 Como Integrar Plugins Parceiros com o `NodeAecGate`

Em comandos do seu plugin (`IExternalCommand`):

```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using NodeAec.Connector.Gate;

[Transaction(TransactionMode.Manual)]
public class MeuComandoRevit : IExternalCommand
{
    private const string ProductSlug = "meu-plugin";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        // Validação local e instantânea (< 1ms, zero rede)
        var check = NodeAecGate.Validate(ProductSlug);
        if (!check.IsLicensed)
        {
            TaskDialog.Show("Node.aec — Licença Necessária",
                $"O produto '{ProductSlug}' não possui licença ativa nesta estação.\n\n" +
                $"Motivo: {check.Message}\n\n" +
                "Abra o Node.aec Connector na Ribbon para entrar com sua conta ou ativar sua licença.");

            NodeAecGate.OpenConnector();
            return Result.Cancelled;
        }

        // Execução normal da funcionalidade
        TaskDialog.Show("Sucesso", $"Executando com licença {check.LicenseType}.");
        return Result.Succeeded;
    }
}
```

---

## 🤝 Como Contribuir

Contribuições da comunidade AEC são muito bem-vindas!
1. Crie uma branch a partir de `main` (`feature/sua-melhoria`).
2. Siga as diretrizes de arquitetura e código descritas em [`AGENTS.md`](AGENTS.md).
3. Certifique-se de que a compilação execute com **0 erros**.
4. Abra um Pull Request detalhando as alterações e o propósito para o ecossistema.

---

## 🌐 Ecossistema Node.aec

- **Portal Oficial**: [nodeaec.com.br](https://nodeaec.com.br)
- **Catálogo de Ferramentas**: [nodeaec.com.br/products](https://nodeaec.com.br/products)
- **Área do Desenvolvedor**: [nodeaec.com.br/workspace](https://nodeaec.com.br/workspace)
- **Suporte & Comunidade**: Abra uma issue neste repositório ou contate o time Node.aec.
