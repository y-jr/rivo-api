# Rivo — Visão de Arquitectura

Destilado de `docs/rivo-arquitetura-global-v1.md` §7 e
`docs/rivo-dados-integracoes-seguranca-v1.md`.

## Estilo

**Monólito modular** — um deployável, decomposto internamente em módulos
alinhados com bounded contexts, com fronteiras internas fortes desenhadas
para permitir extracção futura sem reescrita (ADR-001).

Não é monólito por omissão nem microservices por moda: a decisão está
ancorada em volumetria conhecida (modesta), num ponto de consistência forte
real (Approval → Tesouraria) e no fan-out de `hr.Colaborador`.

## Camadas

Cada módulo organiza-se nas mesmas camadas:

```
API
 ↓
Application
 ↓
Domain

Infrastructure
 ↓
implementa concerns técnicos exigidos por Application e Domain
```

### API

Endpoints HTTP, DTOs de pedido/resposta, validação na fronteira,
autenticação de transporte. **Sem regras de negócio.**

### Application

Casos de uso, serviços aplicacionais, fronteiras de transacção,
orquestração de autorização, contratos publicados para outros módulos,
ports/interfaces de capacidades técnicas.

### Domain

Entidades, agregados, value objects, serviços de domínio, invariantes,
eventos de domínio. **Independente de framework, ORM, HTTP e persistência.**

É aqui que vivem as invariantes de negócio — incluindo as regras de
segregação de funções (ADR-008).

### Infrastructure

Persistência, repositórios, integrações externas, mensageria, background
jobs, storage de ficheiros, infraestrutura de autenticação.

Implementa ports definidos por Application/Domain. Nada depende dela para
fora.

## Direcção de dependências

As dependências apontam **para dentro**, na direcção das regras de negócio.
Detalhe em [dependency-rules.md](dependency-rules.md).

## Módulos

14 módulos, um por bounded context. Ver
[domain/domain-map.md](../domain/domain-map.md) para a classificação
estratégica e [modules/](../modules/) para o detalhe.

**Core:** `finance`, `procurement`, `commercial`, `hr`, `payroll`
**Supporting:** `approval`, `fiscal`, `projects`, `fleet`, `inventory`
**Generic:** `identity`, `audit`, `notifications`, `documents`

**Não são módulos** — Dashboard Executivo, Portal do Colaborador, Portal do
Cliente, Configurações & Administração e Analytics & IA são read models e
canais de apresentação, sem ownership de dados. Ver `domain-map.md`.

## Dados

- Uma base de dados SQL Server, **um schema lógico por domínio**, ownership
  exclusivo de tabela (ADR-002 para o desenho, ADR-029 para o motor).
- Sem `tenant_id`, sem partição multi-tenant (ADR-003).
- Chaves substitutas UUID.
- Concorrência optimista em entidades decididas por mais de uma pessoa.
- `numeric` para valores monetários. Nunca vírgula flutuante.
- Sem eliminação física onde há auditoria ou retenção legal.
- RLS como segunda linha de defesa para segregação de funções — nunca a
  sede da regra (ADR-008).
- Dados fiscais com vigência temporal, nunca em código (ADR-011).

Convenções em [standards/persistence.md](../standards/persistence.md).

## Comunicação entre módulos

Dois mecanismos, escolhidos por caso de uso:

1. **Contrato síncrono** — quando o chamador precisa de resposta imediata
   (ex.: `approval` lê `ReferenciaColaborador` de `hr` para resolver
   aprovadores).
2. **Evento de integração** — quando outro módulo só precisa de reagir a um
   facto (ex.: decisão de aprovação concluída).

Nunca tabelas partilhadas, nunca acesso a repositórios alheios, nunca
`JOIN` entre schemas fora do permitido em ADR-010.

Ver [module-boundaries.md](module-boundaries.md).

## Transacções e consistência

- **Consistência forte** no ponto Approval → Tesouraria: a execução do
  pagamento revalida o estado da decisão e a disponibilidade de caixa. É
  transacção local — uma das razões principais para o monólito modular.
- **Consistência eventual** aceitável em reporting (ex.: actualização de
  "executado vs. planeado" orçamental) e em efeitos secundários não
  críticos.
- Efeitos secundários não críticos (notificações, auditoria assíncrona,
  geração de documentos) ficam **fora** da transacção que regista a decisão
  de negócio. Corrige directamente o anti-padrão encontrado no protótipo
  (trigger que inseria até 20 notificações na mesma transacção).

## Background jobs

Infraestrutura pura. Nunca bloquear um pedido HTTP com: envio de e-mail,
notificações, geração de documentos, importação/exportação CSV, relatórios,
lembretes de SLA, reconciliação bancária, previsões de IA, chamadas a
integrações externas.

## Integrações externas

Cada integração isolada por um **adaptador (Anti-Corruption Layer)** dentro
do módulo que a precisa. Nenhum domínio depende do formato de dados de um
serviço externo.

Mapa completo em `docs/rivo-dados-integracoes-seguranca-v1.md` §2. Principais:
AGT e SAF-T AO (`fiscal`), reconciliação bancária e câmbio (`finance`),
e-mail (`notifications`), gateway de pagamento (`finance`/AR), object storage
(`documents`), modelos de IA (analytics).

## Segurança

Parte da arquitectura, não camada acrescentada depois. Ver
[standards/security.md](../standards/security.md).

## Tecnologia

Backend em **C#/.NET**. **SQL Server** (ADR-002, ADR-029). Frontend
React/Tailwind (documento de produto).

Framework, ORM, tooling de migrações e alojamento estão decididos — ver
[technology-decisions.md](technology-decisions.md), que também lista o que
continua em aberto.
