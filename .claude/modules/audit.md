# audit — Auditoria

**Classificação:** generic domain / infraestrutura.

## Responsabilidade

Registo append-only de acções significativas do sistema: quem fez o quê,
quando, sobre que registo, a partir de que IP.

Contraste deliberado com `approval`: **ambos são transversais, por razões
opostas.** `approval` é transversal porque *é* um domínio com regras
próprias; `audit` é transversal porque **não tem regras de negócio
nenhumas**, só uma garantia técnica uniforme.

Tratá-los da mesma forma seria erro nos dois sentidos.

## Conceitos

| Conceito | Atributos |
|---|---|
| Evento de Auditoria | utilizador, entidade_tipo, entidade_id, acção, valor anterior (JSON em `nvarchar(max)`), valor novo (JSON em `nvarchar(max)`), **ip**, correlation_id, criado em |

A coluna **`ip` é obrigatória por desenho** — o `audit_logs` do protótipo
não a tinha, o que era lacuna face ao requisito absorvido do SGAP.

## Possui

O log. Nunca os dados de negócio que descreve.

## Referência polimórfica — excepção deliberada (ADR-009)

`audit` mantém `entidade_tipo` + `entidade_id` **sem FK**, ao contrário de
`documents`.

Justificação: o log é append-only e tem de sobreviver à eliminação lógica
do registo que descreve, incluindo registar acções sobre entidades que já
não existem. Uma FK real impediria exactamente o que a auditoria precisa de
garantir. Trade-off aceite explicitamente.

## Depende de

**Nada.** É fundacional, tal como `identity`.

Correcção de leitura anterior: dizia-se "depende de `identity` (actor)", mas
isso era conceptual. O actor é guardado como `Guid` — `audit` regista quem
agiu, **não resolve quem essa pessoa é**. Não precisa de referência de
compilação.

É isso que evita um ciclo: `identity` escreve na trilha, logo
`identity → Audit.Contracts`. Se `audit` referenciasse `identity`, os dois
não compilariam.

Não depende do `Domain` nem da `Infrastructure` de nenhum módulo.

## Consumido por

Todos os módulos, por contrato ou evento.

## Contratos publicados

- Registar evento de auditoria.
- Consultar trilha por entidade (relatório).

## Não pode

- Bloquear ou condicionar operações de negócio. Não é mecanismo de
  autorização (isso é `identity`) nem de aprovação (isso é `approval`).
- Copiar automaticamente agregados ou linhas inteiras para o log. O módulo
  de origem decide que informação é relevante e segura para expor.
- Ser duplicado por módulo. O protótipo tinha `audit_logs` e
  `payroll_audit_logs` quase idênticos — não repetir.

## Regras de negócio

- Append-only; não alterável por funcionalidade aplicacional (BR-10).
- Retenção mínima de 10 anos (BR-11).
- Tentativas de acção não autorizada são registadas explicitamente, não
  apenas bloqueadas em silêncio (BR-12).
- Alterações de configuração auditadas com a mesma disciplina que
  transacções de negócio (BR-13).
- Dados sensíveis não são capturados só por existirem no registo de origem.
- O log de auditoria de segurança usa esta mesma capacidade — não uma
  tabela de segurança separada.

## Actores não interactivos

O modelo tem de suportar acções não executadas por um utilizador: jobs
agendados, processos de sistema, integrações automáticas. Continuam
atribuíveis a uma identidade de execução apropriada.

## Perguntas em aberto

- ~~Mecanismo concreto de garantia append-only.~~ **Decidido:** gatilho
  `INSTEAD OF` mais tabela sentinela contra `TRUNCATE` (ADR-029). O que
  continua em aberto é a metade complementar — um utilizador de base de dados
  sem privilégios sobre a tabela.
- Política de retenção diferenciada por módulo/regulação.

## Estado

**Implementado.** Trilha append-only com `AuditEvent` imutável, contrato
`IAuditTrail` em `Rivo.Audit.Contracts`, e consulta filtrada por tipo e
identificador de entidade.

Foi o primeiro consumidor do ADR-017 — `identity` escreve na trilha através do
assembly de contratos, e é isso que impede o ciclo descrito acima.

Verificado em `scripts/verify-audit.ps1` (10 casos).

### Só leitura por HTTP

`GET /audit/entries` é a única superfície pública. **Não existe endpoint de
escrita:** a trilha é escrita pelos módulos através do contrato interno,
nunca por HTTP. Um endpoint público permitiria forjar registos de auditoria,
que é precisamente o que a capacidade existe para impedir.

### Falhas propagam, deliberadamente

Se a escrita da trilha falhar, a operação de negócio é abortada. É o contrário
de `notifications`, onde uma falha de entrega nunca afecta o negócio. O
contraste é intencional: perder auditoria é pior do que falhar ruidosamente;
perder uma notificação não é.

### ⚠ Defeitos conhecidos

- ~~**K9**~~ — **fechado.** O append-only deixou de depender só do código:
  `UPDATE` e `DELETE` são recusados por um gatilho `INSTEAD OF`, e `TRUNCATE`
  pela FK da tabela sentinela `audit_event_truncate_guard` (ADR-029). Fica por
  fazer a metade que depende de privilégios — um utilizador de base de dados
  que não seja dono da tabela.
- **K10** — a escrita da trilha não é transaccional com a operação auditada,
  porque `audit` tem `DbContext` próprio.

Ambos em [state/known-issues.md](../state/known-issues.md).
