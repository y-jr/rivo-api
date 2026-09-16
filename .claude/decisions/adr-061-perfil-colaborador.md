# ADR-061: `Colaborador`, o perfil de quem trabalha cá e não administra nada

## Status

Aceite (2026-09-16). Decisão do utilizador, depois de perguntar: «como é que
funcionários como programador, estagiário ou técnico de TI acessam o portal?»

## Context

Os Perfis de Acesso atribuíveis eram oito: sete de **função** (`Admin`,
`Manager`, `Finance`, `HR`, `Sales`, `AssetManager`, `ProjectManager`) e
`Cliente`, que é a audiência **externa** do Portal do Cliente (ADR-043).

Nenhum significa «é funcionário e vê o que é seu».

Desde o ADR-059 o perfil é **obrigatório no convite** — e essa obrigação foi
deliberada: uma conta sem perfil era o defeito que o registo público deixava.
Mas isso tornou impossível convidar um programador ou um estagiário sem lhe dar
uma de duas coisas erradas:

1. **Um perfil de função que não lhe pertence.** Dar `Sales` a um técnico de TI
   é dar-lhe clientes e facturação.
2. **`Cliente`.** É a audiência externa, e traz `documents.write`.

### O que torna isto quase absurdo

**O Portal do Colaborador não exige permissão nenhuma.** `GET /portal/me` tem
apenas `RequireAuthorization()`: quem vê é quem está **ligado** àquele
colaborador, e o 403 vem de não haver vínculo, não de faltar uma permissão
(ADR-042).

Ou seja: o perfil certo para um funcionário era um perfil **vazio** — e não
existia nenhum, porque todos os que já tinham estado vazios acabaram por receber
permissões quando o seu módulo chegou.

## Requirements

- **Facto** — `/portal/me` autoriza por vínculo (`IEmployeeDirectory.FindByUserIdAsync`),
  não por permissão.
- **Facto** — o ADR-059 tornou o perfil obrigatório ao convidar.
- **Facto** — `Cliente` tem `documents.write` e destina-se a audiência externa.
- **Facto (verificado)** — o catálogo não tinha nenhum perfil vazio disponível.
- **Decisão do utilizador** — criar o perfil, em vez de reaproveitar `Cliente`
  ou reabrir a possibilidade de convidar sem perfil.

## Alternatives

1. **Usar `Cliente` para funcionários.** Rejeitada: é semanticamente errado — a
   mesma etiqueta passaria a significar «cliente externo» e «nosso empregado» —
   e traz uma permissão que o portal não precisa.
2. **Tornar o perfil opcional no convite.** Rejeitada: reabre exactamente o que
   o ADR-059 fechou três dias antes. Contas sem perfil eram o defeito do registo
   público, e voltariam pela porta do lado.
3. **Dar `HR` aos funcionários.** Rejeitada de imediato: daria a cada
   colaborador acesso à folha salarial de toda a gente.
4. **Criar `Colaborador`, vazio** (escolhida).

## Decision

### 1. Um nono perfil atribuível, chamado `Colaborador`

Entra no catálogo como os outros, é semeado no arranque, e aparece em
`GET /identity/roles` e no diálogo de convite. Passa a haver **nove**
atribuíveis e **dez** no total, com `SuperAdmin` que não é atribuível.

### 2. Vazio, e assim deve ficar

Sem uma única permissão. **Não está à espera de módulo nenhum** — é a diferença
entre este e os perfis que estiveram vazios no passado.

A razão é a mesma que torna o portal seguro: ele autoriza por **vínculo**. Quem
vê a assiduidade é quem está ligado àquele colaborador. Uma permissão de módulo
aqui daria acesso a dados de **terceiros** para resolver um problema que o
vínculo já resolve — e, no caso de `hr` ou `payroll`, daria a cada funcionário a
folha salarial dos colegas.

Está escrito no catálogo, em comentário, para ninguém «corrigir» o vazio.

### 3. Continuam a ser dois actos

Convidar dá a conta e o perfil; **ligar ao colaborador** é acto separado
(ADR-054), feito no ecrã de Utilizadores. O perfil diz *o que a conta pode
fazer*; o vínculo diz *por quem ela age* — e desde o ADR-050 é o vínculo que
determina quem aprova.

Este ADR não muda isso. Um `Colaborador` sem vínculo tem conta, entra na
aplicação e não vê nada — que é o comportamento correcto, e o mesmo que qualquer
outro perfil teria.

## Consequences

- **As contagens mudaram** em `verify-authorization` (3, 4, 5, 6),
  `verify-bootstrap` (2, 7) e `verify-settings` (1, 2): oito atribuíveis passam
  a nove, nove totais passam a dez.
- **O tipo `AccessProfileName` do frontend estava desactualizado** e foi
  corrigido de caminho: faltavam-lhe `Cliente` e `SuperAdmin` desde que foram
  criados. Como os perfis chegam em JSON, o compilador nunca teve como
  reclamar — o ecrã de administração mostrava valores que o tipo dizia não
  existirem.
- **Não fecha a lacuna sozinho.** O Portal do Colaborador tem uma rota
  (`/portal/me`); o ecrã do frontend tem cinco secções. Assiduidade, férias,
  documentos e recibos respondem 404. Este ADR dá o perfil; falta a camada de
  composição que sirva os dados restritos ao próprio.

## Risks

- **Um perfil vazio parece um esquecimento.** É o risco de manutenção: alguém
  encontra-o, assume que ficou por preencher, e dá-lhe `hr.employees.read` «para
  o portal funcionar». Isso daria a cada funcionário a ficha de todos os
  colegas. Mitigado por comentário no catálogo e por este ADR, e mais nada — não
  há teste que impeça uma permissão de ser acrescentada.
- **`Colaborador` e `Cliente` são fáceis de confundir** no diálogo de convite,
  sobretudo por quem não conhece a distinção interno/externo. Se aparecerem
  enganos, a mitigação é uma descrição por perfil no ecrã, que hoje não existe.

## Revisit When

- Se os colaboradores passarem a **escrever** pelo portal — pedir férias,
  submeter justificativos —, isto volta a abrir: a escrita pode continuar a
  autorizar-se por vínculo, mas convém rever se `documents.write` passa a ser
  preciso.
- Se surgirem perfis intermédios (um chefe de equipa que vê a assiduidade da sua
  equipa e mais nada), a autorização por vínculo deixa de bastar e passa a ser
  preciso um conceito de âmbito — que hoje não existe.

## Related

ADR-042 (o portal autoriza por vínculo), ADR-043 (`Cliente`, o precedente de
estender o catálogo), ADR-054 (ligar a conta é acto separado), ADR-059 (o perfil
é obrigatório ao convidar), ADR-058 (`SuperAdmin`, que não é atribuível).
