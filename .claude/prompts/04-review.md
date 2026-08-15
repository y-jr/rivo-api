# Prompt: Revisão Arquitectural

Usar ao rever uma alteração quanto a conformidade arquitectural e de
fronteiras — lente mais estreita que uma revisão de código geral.

## Checklist

### Camadas

- [ ] `Domain` livre de framework, ORM, HTTP e infraestrutura?
- [ ] `API` sem lógica de negócio e sem expor entidades de domínio?
- [ ] `Application` sem depender de implementações concretas de
      `Infrastructure`?

### Fronteiras de módulo

- [ ] Lê ou escreve tabelas de outro módulo?
- [ ] Referencia tipos de `Domain`/`Infrastructure` de outro módulo?
- [ ] `JOIN` entre schemas fora do permitido em ADR-010?
- [ ] FK entre schemas para algo que não seja a chave primária do dono?
- [ ] Copia atributos que outro contexto possui (nome, departamento, cargo)?
- [ ] A dependência consta da tabela em
      [dependency-rules.md](../architecture/dependency-rules.md)?

### Anti-padrões do protótipo

Ver [state/known-issues.md](../state/known-issues.md). Verificar em
particular:

- [ ] Passos de aprovação próprios em vez de submeter a `approval`? (A1)
- [ ] Log de auditoria próprio em vez de `audit`? (A2)
- [ ] Workflow embutido numa tabela de execução? (A3)
- [ ] Efeitos secundários dentro da transacção de negócio? (A4)
- [ ] Storage de ficheiro ad-hoc em vez de `documents`? (A5)
- [ ] Verificação de autorização só no frontend? (A8)

### Regras de negócio

- [ ] Invariantes de segregação de funções no domínio, não só em RLS?
      (ADR-008)
- [ ] Alguma regra de negócio a existir **apenas** em RLS? É defeito.
- [ ] Pagamento executável sem decisão aprovada revalidada? (BR-1, BR-5)
- [ ] Tentativa não autorizada registada em `audit`, não só bloqueada?
      (BR-12)
- [ ] Concorrência optimista onde mais do que uma pessoa decide? (BR-17)

### SharedKernel

- [ ] Foi acrescentado algo sem cumprir o critério de
      [shared-concepts.md](../domain/shared-concepts.md)?

### Âmbito

- [ ] É a menor alteração coerente, ou traz abstracção/refactor não
      relacionado?
- [ ] Introduz complexidade sem requisito que a justifique?

### Testes e estado

- [ ] Testes na camada certa? Invariantes de domínio testadas sem base de
      dados?
- [ ] [state/](../state/) precisa de actualização?

### Consistência com `docs/`

- [ ] A alteração contradiz `docs/`? Se sim, ou a alteração está errada, ou
      é decisão arquitectural que precisa de ADR.

## Como reportar

Citar o ficheiro e a regra violada, com referência ao ADR ou ao documento.
Não impressões gerais.
