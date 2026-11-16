# Ressoa — API de Telemetria (.NET)

[![CI](https://github.com/olavoneves/ressoa-api-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/olavoneves/ressoa-api-dotnet/actions/workflows/ci.yml)

> **Matar a larva com som antes que a seca vire epidemia.**
> Global Solution FIAP 2026/2 — CitizenTech · Disciplina **Advanced Business Development with .NET**

O **Ressoa** é um aparelho de ultrassom (ESP32 + transdutor piezoelétrico) que fica dentro do tambor, balde ou caixa d'água e, uma vez por dia, emite um pulso de 18 a 30 kHz que mata as larvas do *Aedes aegypti*, sem veneno. Esta API recebe o **status diário de cada aparelho** (bateria, temperatura da água e resultado do pulso) e responde a pergunta que importa para o morador: **a água da minha casa está protegida hoje?**

**Valor ao cidadão:** o morador de periferia com água intermitente vê no app se o aparelho está funcionando, recebe alertas simples (bateria baixa, sem sinal, pulso falhou, água quente) e o gestor de saúde enxerga onde a proteção parou.

## Integrantes — Grupo **Ressoa** · Turma **2TDSR**

| Nome | RM |
|---|---|
| Olavo Porto Neves | RM563558 |
| Pedro Henrique Dias França | RM561940 |
| Luiz Gustavo Gonçalves | RM564495 |
| Altamir Lima | RM562906 |
| Felipe Conte | RM562248 |

## Arquitetura

```text
[Aparelho ESP32] ──POST /api/v1/leituras (JWT + Idempotency-Key)──┐
                                                                    ▼
[App Mobile] ──GET status / histórico (JWT)──────────► API .NET (Minimal API) ──EF Core──► Oracle
     │                                                    │  (tabelas LEITURA e DISPOSITIVO,
     └──login──► API Java (emite o JWT) ◄──health check───┘   mesmo esquema da API Java)
```

- **ASP.NET Core 10 Minimal API** com *route groups*, validação nativa (`AddValidation` + DataAnnotations/`IValidatableObject`) e respostas em **Problem Details (RFC 9457)**.
- **EF Core 10 + Oracle** (`Oracle.EntityFrameworkCore`) mapeando as tabelas criadas por `scripts/script-bd.sql` do repositório Java (banco único).
- **Um único login:** a API Java emite o JWT; esta API valida o mesmo token (HS256, mesmo `JWT_SECRET`, `iss=ressoa-api`, `aud=ressoa`) e usa os papéis `CIDADAO` e `GESTOR`.

```
src/Ressoa.Telemetria.Api/
├── Domain/        regras de negócio puras (RegrasTelemetria, Leitura, Dispositivo)
├── Application/   casos de uso (LeituraService: idempotência, histórico, status)
├── Contracts/     DTOs de entrada e saída
├── Data/          RessoaDbContext (mapeamento Oracle)
├── Endpoints/     rotas Minimal API
├── Infra/         Auth (JWT), RateLimiting, Health, OpenApi
└── Background/    MonitorSinalService (marca SEM_SINAL)
tools/
└── simulador.cs   simulador de frota para a demonstração
tests/
├── Ressoa.Telemetria.UnitTests/         regras de negócio (xUnit)
└── Ressoa.Telemetria.IntegrationTests/  endpoints com WebApplicationFactory + SQLite
```

## Requisitos da disciplina

| Requisito | Como foi atendido |
|---|---|
| Minimal API RESTful + OpenAPI/Swagger | `Endpoints/TelemetriaEndpoints.cs`; OpenAPI nativo em `/openapi/v1.json` e Swagger UI em `/swagger`, com contratos, parâmetros e respostas documentados |
| Idempotência | Header `Idempotency-Key` obrigatório. Repetição com o mesmo corpo → `200` com a leitura original e `Idempotency-Replayed: true` (nada é gravado de novo); mesma chave com corpo diferente → `422`; requisições simultâneas → chave única `CHAVE_IDEMPOTENCIA` no banco garante uma só gravação |
| Rate limiting com 429 | `Microsoft.AspNetCore.RateLimiting`: janela fixa para telemetria (10/min por usuário) e janela deslizante para consultas (60/min); `429` com `Retry-After` e Problem Details |
| JWT | `Infra/Auth`: valida assinatura, emissor, audiência e expiração; aparelho só aceita leitura do dono ou de `GESTOR` |
| Testes xUnit | 13 testes de unidade (regras) + 17 de integração (endpoints, segurança, 429, health) |
| Health checks | `/health` (tudo), `/health/live`, `/health/ready`: API, **Oracle**, **API Java** e **InfoDengue** |

## Endpoints

| Método e rota | Descrição |
|---|---|
| `POST /api/v1/leituras` | Aparelho envia o status (header `Idempotency-Key`). Identifica o aparelho por `numeroSerie` ou `dispositivoId` |
| `GET /api/v1/dispositivos/{id}/leituras?page&size&de&ate` | Histórico paginado |
| `GET /api/v1/dispositivos/{id}/status` | Último status + `online`, taxa de sucesso do pulso em 7 dias e alertas |
| `GET /health`, `/health/live`, `/health/ready` | Saúde |
| `GET /swagger` | Documentação interativa |

Exemplo de leitura:

```http
POST /api/v1/leituras
Authorization: Bearer <token da API Java>
Idempotency-Key: 9b2f6c1e-3f0a-4c55-9a51-0d7e1b0c2a11
Content-Type: application/json

{ "numeroSerie": "RSA-2026-0001", "bateriaPct": 82, "temperaturaAgua": 24.5, "pulsoOk": true }
```

## Regras de negócio

- Bateria abaixo de **15%** → aparelho `BATERIA_BAIXA` (mesma regra do gatilho `trg_bateria_baixa` do banco); nova leitura com bateria boa → `ATIVO`; `INATIVO` só muda pelo dono.
- Sem status por mais de **26 h** → `SEM_SINAL` (serviço em segundo plano). O pulso diário continua mesmo offline.
- Leituras acumuladas offline são aceitas com até **7 dias** de atraso e não regridem o status atual.

## Como rodar

**Pré-requisitos:** .NET SDK 10 (ou Docker: `docker build -f dockerfiles/Dockerfile -t ressoa-api-dotnet .`) e um Oracle com o esquema do `scripts/script-bd.sql` (repo Java). Para desenvolvimento: `docker compose -f infra/docker-compose.yml up -d oracle` (repositório hub).

```bash
# variáveis (veja .env.example) — nunca versione valores reais
export ConnectionStrings__Oracle="User Id=ressoa;Password=***;Data Source=localhost:1521/FREEPDB1"
export JWT_SECRET="<o mesmo segredo da API Java>"

dotnet run --project src/Ressoa.Telemetria.Api
# Swagger: http://localhost:5xxx/swagger (porta exibida no console)
```

**Testes:** `dotnet test` (não precisam de Oracle: usam SQLite em memória).

Para testar no Swagger: faça login na API Java com `morador@ressoa.app` (usuários de teste no README do repo Java), copie o token e use **Authorize**.

### Simulador de frota (demonstração)

Com as duas APIs no ar, um único comando reproduz os fluxos principais contra a API real:

```bash
dotnet run tools/simulador.cs -- http://localhost:8080 http://localhost:5080 gestor@ressoa.app <senha>
```

| Cenário | O que mostra |
|---|---|
| 1. Dia normal | Cada aparelho envia o status diário → `201` |
| 2. Rede instável | Mesma `Idempotency-Key` reenviada → `200` + `Idempotency-Replayed: true`, sem duplicar |
| 3. Bateria baixa | Status vira `BATERIA_BAIXA` e o alerta aparece em `/status` |
| 4. Sinal voltou | Leituras acumuladas offline são aceitas sem regredir o status atual |
| 5. Rajada | Excede o limite por minuto → `429` com `Retry-After` |

O fluxo completo (login na API Java → leitura na .NET → status refletido na API Java → health `Healthy`) foi validado com Oracle 23 e RabbitMQ.

## Deploy

| Ambiente | URL |
|---|---|
| **API pública (Render)** | https://ressoa-api-dotnet.onrender.com |
| Swagger | https://ressoa-api-dotnet.onrender.com/swagger |
| Saúde | https://ressoa-api-dotnet.onrender.com/health |
| API Java (login/JWT) | https://ressoa-api-java.onrender.com/swagger-ui.html |

Contêiner Docker (`dockerfiles/Dockerfile`) pelo blueprint `render.yaml`, com banco **Oracle FIAP**. Segredos só nas variáveis de ambiente do Render. No plano gratuito a API pode hibernar; a primeira chamada acorda o serviço em alguns segundos.

## Declaração de uso de IA

| Ferramenta | Uso |
|---|---|
| Claude Code (Anthropic) | Apoio na estruturação do projeto, geração e revisão de código, testes e documentação. Todo o código foi revisado e é explicado pela equipe. |
