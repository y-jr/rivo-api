# ADR-058: Conta de super-administração para o arranque circular de BR-20

## Status

Aceite (2026-09-12). Decisão do utilizador: «Eu quero um super admin no
sistema, que terá acesso a tudo e que dará todos os primeiros cargos. Não
será usado por ninguém na empresa, será apenas pra evitar mexer direto na BD
para resolver problemas de permissões iniciais.»

## Context

`pending-decisions.md` §Approval Engine já registava isto, sem solução:

> O que resta é o ovo e a galinha: criar o primeiro Cargo com autoridade
> exige decisão de `approval`, e não há ninguém com autoridade para a tomar.

O `AssignPosition` (ADR-015, BR-20) recusa atribuir directamente um Cargo com
`GrantsApprovalAuthority` — submete sempre a `approval`
(`AssignPositionOutcome.PendingApproval`). E `approval` só decide um pedido
através de uma `ApprovalPolicy` cujo passo aponta para um Cargo **já
ocupado**. Num ambiente novo, nenhum Cargo está ocupado. Não há pedido que
resolva a atribuição do primeiro ocupante, porque resolver esse pedido já
precisaria de um.

Confirmado em código, não apenas em teoria: `BootstrapUserSeeder` só atribui
Perfis de Acesso (RBAC), nunca Cargos — a própria classe documenta a
limitação («Quando existir [o Cargo], é aqui que se estende»). Sem uma via
de saída, o único desbloqueio é escrever directamente na tabela
`hr.position_assignment`, o que esta sessão fez uma vez, manualmente, para
poder testar Férias e Folha Salarial.

## Requirements

- **Facto:** precisa de existir uma forma de atribuir o primeiro Cargo
  aprovador sem depender de `approval` já ter um.
- **Facto (requisito do utilizador):** a conta não é para uso do negócio —
  não deve aparecer como opção no ecrã de Perfis de Acesso, nem ser
  atribuível a outra conta por lá, mesmo por um `Admin`.
- **Inferência:** a operação tem de ficar auditável de forma diferenciada,
  porque contorna deliberadamente uma regra de segregação de funções
  (BR-20) — quem rever a trilha tem de conseguir distinguir isto de uma
  atribuição normal sem ambiguidade.

## Constraints

- Não pode reabrir BR-20 para ninguém além desta conta — nem para `Admin`,
  que continua sujeito à aprovação normal.
- Tem de reutilizar o mecanismo de bootstrap já existente
  (`Bootstrap:Users`), e não inventar um segundo caminho de arranque.

## Alternatives

1. **Estender `BootstrapUserSeeder` para semear directamente uma
   `PositionAssignment` efectiva**, sem conta nem endpoint novo. Resolve o
   arranque uma única vez, mas não cobre o caso descrito por
   `pending-decisions.md` de precisar disto outra vez mais tarde (um Cargo
   aprovador que perde o único ocupante, por exemplo) sem repetir a escrita
   directa na base de dados.
2. **Dar a `Admin` a permissão de atribuir directamente.** Rejeitada: um
   `Admin` de negócio real ganharia o poder de se auto-conceder autoridade
   de aprovação sem decisão de ninguém — exactamente a escalada que o
   ADR-015 fecha.
3. **Conta de operação permanente, perfil próprio, permissão própria** —
   escolhida. Cobre o arranque e qualquer reincidência futura, fica de fora
   do RBAC de negócio, e o custo é uma conta a mais para gerir fora de
   banda.

## Trade-offs

Ganha-se um caminho normal (um ecrã, não uma escrita na base de dados) para
um problema que só acontece raramente mas que bloqueia por completo quando
acontece. Perde-se a garantia — até aqui absoluta — de que nenhuma conta
consegue atribuir autoridade de aprovação sem passar por `approval`; passa a
haver exactamente uma excepção, deliberada e isolada.

## Decision

1. Novo Perfil de Acesso, `AccessProfiles.SuperAdmin` — todas as permissões
   de `Admin`, mais `hr.positions.assign_direct`, que nenhum outro perfil
   tem.
2. Nova permissão, `HrPermissions.PositionsAssignDirect`
   (`hr.positions.assign_direct`) — **fora de `HrPermissions.All`** de
   propósito, para não ser herdada por `Admin` através do
   `.. HrPermissions.All` que compõe o seu catálogo. A policy de autorização
   é registada à parte, em `HrModuleExtensions`, precisamente porque não
   está em `All`.
3. Novo endpoint, `POST /hr/employees/{employeeId}/positions/direct` →
   `AssignPosition.ExecuteDirectAsync` — cria a atribuição já `Effective`,
   sem passar por `IHrApprovalSubmission`, independentemente de o Cargo
   conferir autoridade ou não.
4. Nova acção de auditoria, `HrAuditActions.PositionAssignedDirectly`
   (`hr.position.assigned_directly`), distinta de `PositionAssigned` — quem
   rever a trilha vê de imediato que esta atribuição saltou BR-20, e por
   quem.
5. `AccessProfiles.AssignableProfiles` — o catálogo inteiro menos
   `SuperAdmin`. `ListAccessProfiles` (GET `/identity/roles`) e
   `AssignProfileAsync` (POST `/identity/users/{id}/roles`) passam a usar
   esta lista em vez do catálogo completo: `SuperAdmin` nunca aparece no
   ecrã de administração, e tentar atribuí-lo devolve `400` como se o nome
   não existisse — mesmo vindo de um `Admin`.
6. Conta semeada só por `Bootstrap:Users` (`Bootstrap__Users__2__*`,
   variáveis `BOOTSTRAP_SUPERADMIN_EMAIL`/`_PASSWORD`) — mesmo mecanismo do
   primeiro `Admin` (ADR-016). Não há outra via de criação.

## Consequences

- Fecha o pendente de `pending-decisions.md` §Approval Engine: já não
  precisa de escrita directa na base de dados para desbloquear um ambiente
  novo.
- `Admin` continua, sem excepção, sujeito a BR-20 — a garantia de segregação
  de funções mantém-se intacta para toda a gente do negócio.
- O frontend (`Cargos.tsx`) ganha um controlo extra no diálogo de
  atribuição, visível só a quem tem `hr.positions.assign_direct` — o resto
  da aplicação não muda.
- Verificado ao vivo: `SuperAdmin` atribui um Cargo com autoridade e o
  colaborador fica com `currentPosition` populado de imediato (nunca
  `Pending`); `Admin` recebe `403` no mesmo endpoint e `400` ao tentar
  atribuir o perfil `SuperAdmin` a alguém.

## Risks

- **É uma porta que, se usada por engano ou por má vontade, contorna a
  única salvaguarda contra escalada de privilégios que o sistema tem.** A
  mitigação é estrutural (permissão isolada, perfil não atribuível em
  runtime, auditoria distinta) e não processual — mas a password desta
  conta continua a ser a única barreira real. As credenciais de bootstrap
  em `.env`/`.env.vps` seguem o padrão já existente para `Admin`/decisor
  (visíveis em texto simples nos ficheiros de ambiente) e **têm de ser
  substituídas por uma password forte e única antes de qualquer deployment
  real** — não é diferente do risco já assinalado no ADR-013 para a chave
  de assinatura JWT.
- Se algum dia se automatizar rotação ou expiração de contas, esta conta
  não deve entrar nesse ciclo sem uma segunda decisão — expirá-la sem aviso
  reintroduziria o próprio bloqueio que ela existe para resolver.

## Revisit When

- Se `TaxRateSchedule`/`IncomeTaxSchedule`/`SubsidyExemptionSchedule` (ou
  `Position`) ganharem alguma vez um mecanismo de "fechar a versão
  corrente", o mesmo padrão pode valer a pena revisitar aqui: hoje não há
  forma de desocupar um Cargo aprovador cujo único ocupante saiu, senão por
  esta mesma conta.
- Se a plataforma ganhar múltiplos tenants (fora do âmbito da v1, ADR-003),
  esta decisão tem de ser revista por tenant — hoje assume uma instância
  única.

## Related

ADR-015 (Cargo vs. Perfil; segregação de BR-20), ADR-016 (bootstrap do
primeiro Admin), ADR-034 (motor de `approval`), ADR-050 (quem decide vem do
token), ADR-057 (nenhum acto declara quem o pratica — mesmo espírito:
resolver a identidade/autoridade a partir de algo que o servidor controla,
nunca do que o chamador declara). `pending-decisions.md` §Approval Engine.
