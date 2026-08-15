# Decisões Tecnológicas

_Última actualização: 2026-08-15._

## Decidido

| Área | Decisão | Origem |
|---|---|---|
| Estilo arquitectural | Monólito modular | ADR-001 |
| Linguagem/runtime backend | C# / .NET 10 | `docs/rivo-arquitetura-global-v1.md`, prompt de arquitectura |
| Base de dados | PostgreSQL 17, um schema lógico por domínio | ADR-002 |
| Autenticação | ASP.NET Core Identity | ADR-012 |
| Credencial | JWT bearer com sessão persistida | ADR-013 |
| Fronteira pública de módulo | Assembly `Rivo.X.Contracts` sem dependências | ADR-017 |
| Framework web/API | ASP.NET Core Minimal APIs, sem controllers | ADR-018 |
| Convenções de routing | Um grupo de rotas por módulo, sob o seu prefixo; o host não agrega endpoints | ADR-018 |
| ORM | EF Core com Npgsql, um `DbContext` por módulo | ADR-019 |
| Nomes na base de dados | `snake_case` via `EFCore.NamingConventions` | ADR-019 |
| Versão do UUID | UUIDv7 (`Guid.CreateVersion7`) — ordenado no tempo | ADR-019, estende ADR-002 |
| Tooling de migrações | Migrações EF Core por módulo, histórico próprio em cada schema | ADR-020 |
| Ambiente local / containerização | Docker Compose; imagem multi-fase, utilizador não-root | ADR-021 |
| Framework de teste | xUnit v2.9.3, sem biblioteca de asserções | ADR-022 |
| Estrutura de testes | Um projecto por domínio de módulo, em `tests/Modules/` | ADR-022 |
| Integração contínua | GitHub Actions, dois jobs separados | ADR-023 |
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

## Em aberto

Nenhuma destas deve ser assumida ao implementar. Se for necessária para
avançar, decidir explicitamente e registar ADR.

- **CD, ambientes e alojamento** — o CI está fechado (ADR-023); publicar não.
  Arrasta consigo o passo de migrações em produção (ADR-020).
- Frameworks de teste de integração com infraestrutura real (candidato:
  Testcontainers). O domínio está fechado pelo ADR-022.
- Tooling de testes de arquitectura (imposição de fronteiras).
- Gestão central de versões de pacotes em `src/` (`tests/` já está resolvido).
- Mecanismo geral de despacho de eventos entre módulos (o worker de
  `notifications` resolve só o caso dele).
- Aplicação de migrações em produção — hoje só acontece no arranque, em
  `Development`.
- Mecanismo de MFA.
- Provider de e-mail transaccional — o canal actual escreve em log (K13).
- Serviço de object storage para `documents` — hoje é sistema de ficheiros.
- Gestão de segredos em produção.
- Gateway de pagamento (mercado angolano).
- Fonte da taxa de câmbio (candidato: BNA).
- Provider de modelos de IA.

Detalhe e dependências em
[state/pending-decisions.md](../state/pending-decisions.md).

## Fora de âmbito da v1

- Iniciação de pagamentos electrónicos junto da banca (Fase 2 no SGAP).
- Integração com software de contabilidade externo (Fase 2).
- Multi-tenancy (ADR-003).
