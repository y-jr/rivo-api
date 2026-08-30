# Regras de Dependência

## Direcção entre camadas

```
API → Application → Domain
Infrastructure → Application / Domain
```

As dependências de **código-fonte** apontam para dentro. Isto não descreve
a ordem de execução em runtime.

### API

Pode depender de: contratos, DTOs e casos de uso de `Application`.

Não pode: conter regras de negócio; depender de `Infrastructure`; expor
entidades de `Domain` como modelos de transporte.

**Excepção: o composition root.** `Rivo.Api` é o *host*, não a camada API de
um módulo. Referencia a `Infrastructure` de cada módulo porque tem de
registar implementações concretas no contentor de DI. A alternativa seria
varrimento de assemblies — mais magia, não menos acoplamento.

A regra vale integralmente para a camada API de cada módulo
(`Rivo.Identity.Api` e equivalentes), que nunca referencia `Infrastructure`.

### Application

Pode depender de `Domain`. Define os ports que `Infrastructure` implementa
(repositórios, gateways, relógio, serviços externos).

Não pode depender de implementações concretas de `Infrastructure`.

### Domain

Não depende de nada fora de si. Sem HTTP, sem ORM, sem framework, sem SDKs
externos, sem `Infrastructure`.

Se uma invariante de negócio precisa de base de dados para ser testada, ela
vazou da camada de domínio.

### Infrastructure

Implementa ports de `Application`/`Domain`. Nada depende dela para fora.

## Direcção entre módulos

Um módulo só depende de outro através do seu **assembly de contratos**
(`Rivo.X.Contracts`) — ADR-017.

Proibido depender de outro módulo através de:

- `Application`, `Domain` ou `Infrastructure`
- repositórios internos
- tabelas de base de dados
- handlers aplicacionais internos

Os assemblies de contratos não dependem de nada. É isso que impede ciclos
por construção: `A → B.Contracts` e `B → A.Contracts` coexistem sem
referência circular.

## Persistência entre schemas

Regra de ADR-010, aplicável a todos os módulos:

- **Permitido:** FK entre schemas **exclusivamente para a chave primária do
  contexto dono**, só para integridade referencial.
- **Proibido:** FK para colunas que não sejam a chave primária do dono;
  `JOIN` a outras tabelas do contexto dono; FK no sentido inverso ao da
  dependência declarada.
- Ler um atributo pertencente a outro contexto faz-se pelo contrato
  publicado, não por SQL.

Numa extracção futura, estas FKs degradam-se para identificadores simples.

## Dependências permitidas entre módulos

Direcções declaradas. Qualquer dependência fora desta tabela precisa de
justificação e, se não for trivial, de ADR.

| Módulo | Pode depender de | Estado |
|---|---|---|
| `audit` | — (fundacional) | implementado |
| `hr` | audit, documents | implementado; por ligar: approval, notifications |
| `identity` | audit, hr, documents, notifications | implementado |
| `payroll` | hr, finance, fiscal, approval, documents, audit, notifications | IRT/INSS ligado a `fiscal` (2026-08-30); Recibo ligado a `documents` (2026-08-30); resto por implementar |
| `finance` | procurement, commercial, hr, approval, fiscal, documents, audit, notifications | por implementar |
| `procurement` | hr, approval, documents, audit, notifications | por implementar |
| `commercial` | hr, fiscal, approval, documents, audit, notifications | por implementar |
| `approval` | identity, hr, finance (leitura orçamental), audit, notifications | por implementar |
| `fiscal` | finance, commercial, procurement, inventory, payroll — **apenas para relato** (ver nota) | por implementar |
| `projects` | hr, finance, commercial, documents, audit, notifications | por implementar |
| `fleet` | hr, finance, inventory, documents, audit, notifications | por implementar |
| `inventory` | procurement, finance, documents, audit, notifications | por implementar |
| `documents` | audit | implementado |
| `notifications` | — (fundacional) | implementado |

Todas as dependências acima são sobre o assembly de **contratos** do módulo
alvo, nunca sobre a sua implementação.

**Notas de risco:**

- **`hr` ↔ `approval` é dependência mútua.** `hr` submete férias e
  atribuições de Cargo (ADR-015); `approval` lê Cargo para resolver
  aprovadores. Em .NET, uma referência mútua de projectos não compila.

  **Já resolvido estruturalmente:** `Rivo.Hr.Contracts` existe e não depende
  de nada. Quando `approval` for implementado, publica
  `Rivo.Approval.Contracts` nos mesmos termos, e os dois referenciam-se pelos
  contratos. Ver ADR-017.

- `approval → hr` e `approval → finance` são as duas leituras onde nasce o
  "God Module". Têm de ser contratos estreitos, explícitos e versionados —
  nunca leitura livre.

- `fiscal` tem **duas direcções distintas**, que não devem ser confundidas:
  - **Determinação fiscal** — os módulos transaccionais consultam `fiscal`.
    Nesta direcção `fiscal` não depende deles.
  - **Relato e exportação** (SAF-T AO, declarações) — `fiscal` lê os dados
    que tem de reportar. Nesta direcção depende deles, por contratos
    publicados, nunca por acesso directo a tabelas.

  Confundir as duas produz um de dois erros: ou um motor fiscal acoplado a
  todos os contextos, ou a impossibilidade de gerar SAF-T.

## Dependências circulares

Não são permitidas.

Se A precisa de B e B precisa de A, parar e reavaliar. Resoluções
possíveis: mover a responsabilidade para o seu dono real; substituir
acoplamento síncrono por evento; introduzir um contexto dedicado;
remodelar a dependência.

Com assemblies de contratos (ADR-017), o caso comum resolve-se sozinho.

O resultado de uma resolução não trivial regista-se como ADR.

## SharedKernel

Pode ser referenciado por módulos. Não depende de módulos nem de
`Infrastructure`. Ver [domain/shared-concepts.md](../domain/shared-concepts.md).

Continua vazio — nada foi ainda justificado para lá.

## Capacidades transversais

`approval`, `documents`, `notifications` e `audit` não são atalho para
contornar fronteiras. Expõem contrato explícito; a regra de negócio que
determina *quando* e *porquê* são usados permanece no módulo de origem.

## Imposição

Três camadas, da mais forte para a mais fraca:

1. **O compilador.** Desde ADR-017, um módulo que só publica contratos não
   pode ter as suas internals referenciadas.
2. **Os testes de arquitectura** (ADR-024), em
   `tests/Rivo.Architecture.Tests`, corridos pelo CI a cada PR. Verificam as
   referências declaradas nos `.csproj` **e** o que os assemblies
   efectivamente usam — a distinção importa, porque o compilador poda
   referências que nenhum tipo usa, e uma referência declarada e não usada
   fica invisível a quem só olha para os assemblies.
3. **A revisão humana**, para o que resta.

**A tabela acima tem um equivalente executável** em
`ProjectReferenceTests.DependenciasDeclaradas`. Acrescentar uma direcção aqui
sem a acrescentar lá faz o teste falhar — e é essa a intenção: uma direcção
nova é decisão arquitectural, não um detalhe de implementação.

O que continua por rever manualmente: que os contratos não engordem até serem
a `Application` inteira. Que não exponham entidades de domínio **já é
verificado** (`Contracts_ExposeNoTypeFromAnotherRivoAssembly`).


