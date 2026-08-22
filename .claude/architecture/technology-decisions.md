# Decisões Tecnológicas

_Última actualização: 2026-08-15._

## Decidido

| Área | Decisão | Origem |
|---|---|---|
| Estilo arquitectural | Monólito modular | ADR-001 |
| Linguagem/runtime backend | C# / .NET 10 | `docs/rivo-arquitetura-global-v1.md`, prompt de arquitectura |
| Base de dados | SQL Server externo, um schema lógico por domínio | ADR-002 (desenho), ADR-029 (motor) |
| Autenticação | ASP.NET Core Identity | ADR-012 |
| Credencial | JWT bearer com sessão persistida | ADR-013 |
| Fronteira pública de módulo | Assembly `Rivo.X.Contracts` sem dependências | ADR-017 |
| Framework web/API | ASP.NET Core Minimal APIs, sem controllers | ADR-018 |
| Convenções de routing | Um grupo de rotas por módulo, sob o seu prefixo; o host não agrega endpoints | ADR-018 |
| ORM | EF Core com `Microsoft.EntityFrameworkCore.SqlServer`, um `DbContext` por módulo | ADR-019, ADR-029 |
| Nomes na base de dados | `snake_case` via `EFCore.NamingConventions` | ADR-019 |
| Versão do UUID | UUIDv7 (`Guid.CreateVersion7`) — ordenado no tempo | ADR-019, estende ADR-002 |
| Tooling de migrações | Migrações EF Core por módulo, histórico próprio em cada schema; aplicadas no arranque por interruptor | ADR-020, ADR-030 |
| Ambiente local / containerização | Docker Compose (`docker-compose.dev.yml` acrescenta o SQL Server); imagem multi-fase, utilizador não-root | ADR-021, ADR-029 |
| Framework de teste | xUnit v2.9.3, sem biblioteca de asserções | ADR-022 |
| Estrutura de testes | Um projecto por domínio de módulo, em `tests/Modules/` | ADR-022 |
| Integração contínua | GitHub Actions, dois jobs separados | ADR-023 |
| Testes de arquitectura | Reflexão e leitura de `.csproj`, sem biblioteca | ADR-024 |
| Testes de integração | Testcontainers com SQL Server real | ADR-026, ADR-029 |
| Chaves primárias | UUID (chave substituta) | ADR-002 |
| Concorrência optimista | Coluna `version` | ADR-002 |
| Valores monetários | `numeric` — nunca vírgula flutuante | ADR-002 |
| Taxas e escalões fiscais | Dados com vigência temporal, nunca código | ADR-011 |
| Multi-tenancy | Nenhuma na v1 | ADR-003 |
| Frontend | React + Tailwind CSS | Documento de produto |
| Tempo real | WebSockets | Documento de produto |
| API | REST | Documento de produto |
| Storage de documentos | Object storage compatível com S3 (ou equivalente) | `docs` §2.2 — inferência |
| Isolamento de integrações | Anti-Corruption Layer por integração | `docs` §2.1 |
| Alojamento e CD | VPS com Docker Compose, publicado por SSH em `push` para `main` | ADR-031 |
| Gestão de segredos | Ficheiro `.env` na máquina de destino, fora do repositório | ADR-031 |
| Gestão de versões de pacotes | Central, em `Directory.Packages.props` na raiz | — |

## Em aberto

Nenhuma destas deve ser assumida ao implementar. Se for necessária para
avançar, decidir explicitamente e registar ADR.

- Observabilidade em produção — hoje são `docker compose logs` numa máquina
  (ADR-031).
- Mecanismo geral de despacho de eventos entre módulos (o worker de
  `notifications` resolve só o caso dele).
- Mecanismo de MFA.
- Provider de e-mail transaccional — o canal actual escreve em log (K13).
- Serviço de object storage para `documents` — hoje é sistema de ficheiros.
- Utilizador de base de dados restrito aos schemas do Rivo — a base de dados é
  partilhada com outros sistemas (ADR-029).
- Cópia de segurança do volume de documentos da VPS (ADR-031).
- Gateway de pagamento (mercado angolano).
- Fonte da taxa de câmbio (candidato: BNA).
- Provider de modelos de IA.

Detalhe e dependências em
[state/pending-decisions.md](../state/pending-decisions.md).

## Fora de âmbito da v1

- Iniciação de pagamentos electrónicos junto da banca (Fase 2 no SGAP).
- Integração com software de contabilidade externo (Fase 2).
- Multi-tenancy (ADR-003).
