# ADR-001: Estilo Arquitectural — Monólito Modular

## Status

Aceite

## Context

O Rivo é uma plataforma de gestão empresarial greenfield que cobre múltiplos
domínios de negócio e que deve absorver progressivamente as capacidades do
SGAP até este se tornar funcionalmente obsoleto.

O protótipo existente demonstrou o problema a resolver: 211 tabelas, 5
implementações paralelas de aprovação, 2 de auditoria, RBAC inconsistente
entre o documento de produto e o código. **O problema nunca foi de escala —
foi de ausência de fronteiras internas e de ownership.**

Análise completa em `docs/rivo-arquitetura-global-v1.md` §7.

## Requirements

- **Facto** — Volumetria do SGAP: ~500 processos/dia, ~10.000 pagamentos/mês.
  Descreve a carga de uma organização.
- **Facto** — Escalabilidade exigida: ≥5.000 processos/ano sem redesenho.
  Número modesto.
- **Facto** — Disponibilidade: 99,5% em horário alargado (SGAP RNF-004).
- **Facto** — Consistência: bloqueio técnico de pagamento fora do estado
  Aprovado; revalidação de estado e saldo na execução (RN-001, RN-020).
- **Inferência** — Isolamento de falhas: uma falha em Pagamentos não deve
  indisponibilizar Projectos, Frota ou os portais.
- **Hipótese** — Dimensão da equipa de engenharia não confirmada.

## Constraints

- Empresa única, sem multi-tenancy (ADR-003).
- Um PostgreSQL (ADR-002).
- `hr.Colaborador` tem o maior fan-out do sistema — é referenciado por
  Approval, Procurement, Financeiro, Projectos, Frota e Comercial.

## Alternatives

1. **Monólito modular bem estruturado**, com fronteiras internas fortes.
2. **Monólito "solto"** — módulos sem fronteiras impostas.
3. **Microservices**.
4. Híbridos: monólito modular com background workers e/ou eventos
   assíncronos.

## Trade-offs

| Eixo | Monólito modular | Microservices |
|---|---|---|
| Deployment | Um deployável; qualquer alteração exige deploy do todo | Independente por serviço — útil só se houver necessidade real |
| Consistência | Transacção local trivial em Approval→Tesouraria | Exige saga/outbox para satisfazer um requisito que hoje é local |
| Comunicação | Chamada em processo, interface interna estável | Rede: idempotência, retries, versionamento |
| Isolamento de falhas | Por disciplina de dependências — possível, não automático | Físico "de fábrica", ao custo de toda a complexidade operacional |
| Ownership de dados | Por disciplina interna | Forçado pela separação física |
| Custo operacional | Baixo | Alto |
| Extracção futura | Possível **se** desenhado com fronteiras fortes desde o início | N/A |

O monólito "solto" foi rejeitado por ser exactamente o que produziu os
problemas do protótipo.

## Decision

**Monólito modular bem estruturado, com fronteiras internas fortes,
desenhado para permitir extracção futura.**

## Consequences

Facilita:

- O ponto de maior exigência de consistência do sistema (Approval →
  Tesouraria) resolve-se numa transacção local.
- `hr.Colaborador`, com o maior fan-out, não fica atrás de uma fronteira de
  rede — evita latência e falhas parciais em quase todos os fluxos.
- Custo operacional baixo: uma base de dados, um runtime.
- RLS e RBAC garantidos de forma consistente a partir de um único ponto de
  aplicação.

Dificulta / exige disciplina desde o dia um:

- Ownership de dados por módulo estritamente respeitado, sem tabelas
  partilhadas e sem `JOIN` entre schemas fora do permitido (ADR-010).
- Comunicação entre módulos por interfaces internas estáveis.
- Nenhum módulo independente (Projectos, Frota, Portais) pode depender em
  runtime do caminho crítico de `approval` ou `finance`.
- Efeitos secundários não críticos fora da transacção que regista a decisão
  de negócio.

## Risks

- **Erosão de fronteiras.** Sem processo forçado, a disciplina degrada-se —
  foi o que aconteceu no protótipo. Mitigação: testes de arquitectura
  automatizados assim que a stack estiver fechada; até lá, revisão contra
  `architecture/dependency-rules.md`.
- **Deploy acoplado.** Uma alteração num módulo obriga a deploy do todo.
  Aceitável para a volumetria e cadência conhecidas.

## Revisit When

- Houver evidência de necessidade de escalar um módulo independentemente.
- A equipa crescer para múltiplas equipas com ciclos de release
  independentes.
- A volumetria crescer uma ordem de grandeza acima do previsto.
- O requisito de disponibilidade divergir muito entre módulos.

## Related

- `docs/rivo-arquitetura-global-v1.md` §7
- [ADR-002](adr-002-database.md), [ADR-003](adr-003-no-multi-tenancy.md),
  [ADR-010](adr-010-referencia-entre-contextos.md)
- [architecture/architecture.md](../architecture/architecture.md)
