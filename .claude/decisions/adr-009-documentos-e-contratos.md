# ADR-009: Documentos como Capacidade Transversal, com Ligação no Contexto de Origem

## Status

Aceite (decisões D5 e D7, fechadas pelo cliente; resolução R3)

## Context

**D5 — Documentos.** No protótipo, cada módulo reinventava "guardar um
ficheiro": `generated_documents`, `document_templates`, `document_types`,
`client_documents`, `employee_documents`, mais colunas ad-hoc `file_url`,
`pdf_path`, `file_path`, `document_url` espalhadas por outras tabelas.

**D7 — Contratos.** Três conceitos sem relação entre si: `contracts`
(trabalho, RH), `commercial_contracts` (venda, Comercial) e
`legal_contracts` (genérico multi-parte).

**R3 — Objecção em revisão.** O desenho inicial resolvia D5 com uma
capacidade `documents` única usando FK polimórfica (`entidade_tipo` +
`entidade_id`). Isso perde integridade referencial, precisamente num domínio
com requisitos fortes de retenção legal.

## Requirements

- **Facto** — Retenção mínima de 10 anos para auditoria; prazos legais
  angolanos para documentos fiscais.
- **Facto** — Sem eliminação física de dados sujeitos a retenção.
- **Facto** — Cifra em repouso (AES-256) para anexos.

## Constraints

`documents` não pode depender dos módulos consumidores — inverteria a
direcção de dependência de uma capacidade transversal.

## Alternatives

Para a ligação documento ↔ registo de negócio:

1. **Tabela de ligação no contexto de origem**, com FKs reais.
2. FK polimórfica em `documents` (`entidade_tipo` + `entidade_id`).
3. Coluna de documento em cada tabela de negócio (o padrão do protótipo).

A opção 3 é o problema a corrigir. A opção 2 mantém `documents`
independente mas sem integridade referencial e sem lugar natural para a
retenção legal.

## Trade-offs

A opção 1 custa uma tabela pequena por contexto consumidor. Em troca dá
integridade nos dois sentidos e coloca a retenção onde ela é conhecida.

## Decision

### Documentos

`documents` é capacidade transversal única. Possui o ficheiro, os
metadados, o ponteiro de storage, o hash e o versionamento. **Não referencia
nenhum domínio.**

A ligação a um registo de negócio vive no **contexto de origem**, numa
tabela de ligação própria com FKs reais para o seu registo e para
`documents.documento(id)`:

```
hr.colaborador_documento(colaborador_id → hr.colaborador,
                         documento_id  → documents.documento,
                         categoria)
```

A **classificação e a retenção legal ficam no contexto de origem** — só ele
sabe o prazo aplicável àquele tipo de documento. `documents` não pode
sabê-lo genericamente.

Direcção de dependência preservada: o consumidor depende de `documents`;
`documents` não depende de ninguém.

### Excepção: `audit`

`audit` **mantém** referência polimórfica (`entidade_tipo` + `entidade_id`,
sem FK).

Justificação: o log é append-only e tem de sobreviver à eliminação lógica do
registo que descreve, incluindo registar acções sobre entidades que já não
existem. Uma FK real impediria exactamente o que a auditoria precisa de
garantir. **Trade-off aceite explicitamente.**

### Contratos

Três conceitos distintos, com donos distintos, **não fundidos**:

| Conceito | Dono |
|---|---|
| Contrato de Trabalho | `hr` |
| Contrato Comercial | `commercial` |
| "Documento legal" | não é entidade própria — é um Documento com `categoria = legal`, ligado ao contrato respectivo pela tabela de ligação do seu contexto |

`legal_contracts` do protótipo **não** se torna uma quarta entidade.

## Consequences

Facilita:

- Fim da fragmentação de storage de ficheiros.
- Integridade referencial nos dois sentidos.
- Retenção legal no contexto que a conhece.
- Contratos com semântica limpa por contexto.

Dificulta:

- Uma tabela de ligação por contexto consumidor.
- Consultas transversais ("todos os documentos do sistema por categoria")
  exigem união entre tabelas de ligação, não uma única query.

## Risks

- **Reincidência:** um módulo voltar a acrescentar uma coluna `file_url`
  própria. Mitigação: proibição explícita em
  [standards/persistence.md](../standards/persistence.md) e em
  [modules/documents.md](../modules/documents.md).
- Documentos órfãos se uma tabela de ligação for esquecida. Mitigação:
  auditoria periódica de documentos sem ligação.

## Revisit When

- Surgir requisito de pesquisa transversal de documentos com performance
  incompatível com a união de tabelas de ligação.

## Related

- `docs/rivo-arquitetura-global-v1.md` §10 (R3), §9 (D5, D7)
- [modules/documents.md](../modules/documents.md),
  [modules/audit.md](../modules/audit.md)
- [standards/persistence.md](../standards/persistence.md)
