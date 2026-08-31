# ADR-042: Portal do Colaborador — "Próprio" por Vínculo Identity → Employee

## Status

Aceite (2026-08-31). Decisão do utilizador, em resposta directa à escolha de
por onde continuar a Fase 8.

## Context

A Fase 8 (`roadmap-execucao.md`) tem cinco camadas de composição por
implementar. O Portal do Colaborador (`docs/rivo-suite-descricao-modulos.md`
§11) é a segunda, depois de Configurações & Administração (ADR-041). O que o
distingue das outras quatro é que **precisa de saber quem é "o próprio"** —
o colaborador correspondente à conta autenticada — antes de poder mostrar
seja o que for.

`hr.Employee` já tem um campo `UserId` (opcional, ADR-004: nem todo o
colaborador tem login, nem todo o login é colaborador) desde a Fase 0, e já
está publicado em `EmployeeReference.UserId` (`Rivo.Hr.Contracts`). Nunca
teve, até agora, um consumidor que precisasse de resolver o sentido inverso
— dado um utilizador, qual é o seu colaborador.

## Decision

**"Próprio" é uma regra de contexto, resolvida pelo vínculo
Identity → Employee — nunca uma permissão.**

- **`CurrentUser` (a identidade autenticada) é a fonte de verdade.** O
  colaborador é resolvido a partir dela, nunca o inverso.
- **Não se cria `hr.employees.read_own` nem equivalente.** Uma permissão
  responderia "que módulos/operações este perfil pode usar" — que é a
  pergunta errada aqui. "Ver os meus próprios dados" não é uma operação que
  se atribui a um perfil; é uma consequência de estar autenticado como
  quem quer que seja.
- **Sem colaborador ligado, o portal devolve 403 — nunca tenta adivinhar.**
  Uma conta sem `Employee.UserId` correspondente não tem "o próprio" para
  mostrar. Não se cai para um colaborador por nome parecido, por e-mail, ou
  por qualquer heurística — a ausência de vínculo é um estado a recusar
  explicitamente, não um problema a contornar em silêncio.
- **Admin continua a usar os fluxos administrativos existentes.** O portal
  não é um atalho de RBAC: `GET /portal/me` não aceita `employeeId` nenhum
  — devolve sempre e só o colaborador do próprio chamador. Para ver dados de
  terceiros, os endpoints de `hr` com `hr.employees.read` continuam a ser o
  caminho.
- **A camada de composição consome o contrato publicado, nunca as tabelas.**
  `IEmployeeDirectory.FindByUserIdAsync` (novo, `Rivo.Hr.Contracts`) é o
  único caminho — mesma disciplina de ADR-010, agora também na direcção
  "identidade autenticada → colaborador".

**Consequência que se seguiu, não pedida mas necessária para a decisão
acima ser segura:** `Employee.UserId` passou a ser único quando preenchido
(índice filtrado em `HrDbContext`, mais a verificação em `HireEmployee`).
Até agora ninguém confiava em "no máximo um colaborador por conta" — o
campo existia, mas sem consumidor a assumi-lo. Resolver "o próprio" por
`FirstOrDefault` sobre um campo que tolerava duplicados exporia dados de um
colaborador a outra conta ligada ao mesmo `UserId`, por acidente.

## Consequences

### O que fica mais fácil

- Qualquer capacidade futura do Portal do Colaborador (recibos, férias,
  assiduidade, documentos pessoais) resolve "o próprio" da mesma forma,
  sem reabrir esta decisão.
- `hr` ganha um segundo sentido de leitura no seu contrato — útil também
  fora do portal, se algum dia outro consumidor precisar de "a quem
  pertence esta conta".

### O que fica em aberto, e é assumido

- **Só a resolução de "o próprio" está feita, não o portal inteiro.** A
  primeira aplicação concreta (`GET /portal/me`) devolve um resumo do
  colaborador — nome, estado, departamento, cargo actual. Recibos, férias,
  assiduidade e o resto de `docs/rivo-suite-descricao-modulos.md` §11
  continuam por fazer, cada um como o seu próprio incremento sobre este
  mecanismo.
- **`Employee.LinkToUser` continua sem endpoint** — `UserId` só se define
  hoje na contratação (`POST /hr/employees`). Ligar a conta depois é
  capacidade separada, sem consumidor ainda.

## Related

ADR-041 (padrão de camada de composição), ADR-010 (referência por
identificador entre contextos), ADR-004 (Identity ≠ Employee),
`modules/hr.md`, `domain/domain-map.md` §Read models,
`state/roadmap-execucao.md` Fase 8.
