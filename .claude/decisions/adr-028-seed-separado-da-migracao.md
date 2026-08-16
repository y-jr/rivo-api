# ADR-028: Seed Separado da Migração

## Status

Aceite (2026-08-16)

## Context

O ADR-020 tirou a migração do arranque da aplicação, por duas razões
concretas: várias instâncias competiriam pelo mesmo schema, e uma migração
destrutiva correria sem ninguém a aprovar. Ficou restrita a `Development`, e em
produção passou a ser passo de pipeline.

**O seed foi atrás, sem que ninguém decidisse isso.** Estava no mesmo método
— `InitialiseIdentityModuleAsync` migrava *e* semeava — e o gate de
`Development` apanhou os dois.

O preço apareceu no primeiro deployment real. Staging ficou com todas as
tabelas, todos os schemas, os gatilhos de append-only e as colunas de
concorrência — **e nenhum Perfil de Acesso, nenhum administrador**. A
aplicação respondia, autenticava utilizadores registados de novo, e não tinha
ninguém com autoridade para fazer seja o que for.

Consequência prática: as seis suites de verificação não podiam correr contra
staging, porque autenticam-se como o administrador de arranque. O critério de
saída da Fase 1 ficou inalcançável por um acidente de implementação.

## Requirements

- **Facto** — ADR-020: migrar automaticamente no arranque é perigoso em
  produção.
- **Facto** — ADR-016: o seed é **idempotente** e nunca altera contas
  existentes, incluindo passwords.
- **Facto** — As suites de verificação autenticam-se como o administrador de
  arranque.
- **Facto** — `standards/security.md` exige que credenciais não vivam em
  configuração versionada.
- **Inferência** — Um ambiente sem administrador não é utilizável, mesmo que
  responda a pedidos.

## Alternatives

1. **Separar: migração no pipeline, seed no arranque fora de Production**
   (escolhida).
2. Passo de seed no pipeline, a par das migrações.
3. Deixar como estava e criar o administrador à mão em cada ambiente.
4. Permitir migração e seed no arranque em staging.

A opção 2 é defensável e seria a escolha certa se o seed tivesse os riscos da
migração. Rejeitada por custo desproporcionado: exigiria um ponto de entrada de
linha de comandos na aplicação, que não existe, só para replicar o que o
arranque já faz de forma segura.

A opção 3 é o que estava a acontecer por omissão, e é exactamente o género de
passo manual não registado que produz ambientes que divergem entre si.

A opção 4 traria de volta o risco que o ADR-020 tinha eliminado — e traria-o
por arrasto, que é a pior forma de o reintroduzir.

## Decision

**Separar as duas operações. São coisas diferentes e o gate é diferente.**

| Operação | Onde corre | Porquê |
|---|---|---|
| **Migração** | Pipeline, sempre; arranque só em `Development` | Instâncias concorrentes; migração destrutiva sem aprovação (ADR-020) |
| **Seed** | Arranque, em tudo excepto `Production` | Idempotente e aditivo; nenhum dos riscos acima |

`InitialiseXModuleAsync` passa a `MigrateXModuleAsync`, e `identity` ganha um
`SeedIdentityModuleAsync` à parte.

### Porque é que o seed não carrega os riscos da migração

- **Instâncias concorrentes:** o seed verifica antes de escrever. Duas
  instâncias em simultâneo produzem o mesmo estado final.
- **Destrutivo sem aprovação:** o seed nunca altera nem apaga. Cria o que
  falta, e ADR-016 é explícito em que não repõe passwords de contas
  existentes.

### Produção fica de fora

Não porque o seed seja perigoso, mas porque **criar o primeiro administrador de
produção deve ser acto deliberado e auditado**, não efeito colateral de um
arranque. Como chega lá é decisão por tomar, e continua em
`pending-decisions.md`.

### Credenciais de arranque em staging

A password vai para o Key Vault e chega à aplicação por referência — nunca por
app setting em claro, nunca em ficheiro versionado. É a credencial que abre o
sistema inteiro na primeira utilização.

## Consequences

Facilita:

- Um ambiente novo fica **utilizável** ao subir, não apenas responsivo.
- As suites de verificação passam a poder correr contra staging, que é o
  critério de saída da Fase 1.
- A distinção entre "esquema" e "dados de arranque" fica explícita no código,
  em vez de acidental.

Dificulta / exige:

- Um método a mais por módulo que venha a ter seed.
- A ordem passa a ser responsabilidade de quem chama: o seed pressupõe que as
  migrações já correram. Em `Development` isso é garantido pela ordem em
  `Program.cs`; em staging, por o pipeline migrar antes de trocar a imagem.

## Risks

- **Arranque a semear antes de a migração do pipeline terminar.** Se um
  deployment trocar a imagem enquanto as migrações ainda correm, o seed falha
  ao encontrar tabelas em falta. Hoje não acontece — o pipeline migra antes de
  publicar a imagem — mas é acoplamento de ordem que depende do workflow e não
  do código.
- **Produção sem caminho de arranque.** Fica deliberadamente sem resposta, e é
  uma lacuna real que a fase de produção tem de fechar.
- **A credencial de arranque em staging é conhecida por quem lê o Key Vault.**
  É inerente a qualquer bootstrap (ADR-016 §Consequences) e não muda com esta
  decisão.

## Revisit When

- Produção existir — obriga a decidir como o primeiro administrador é criado
  lá.
- Outro módulo precisar de seed, e a ordem entre seeds passar a importar.
- O arranque passar a ter mais do que uma instância em staging, altura em que
  vale a pena confirmar que a idempotência do seed aguenta a concorrência real.

## Related

- [ADR-016](adr-016-bootstrap-autoridade.md) — o seed cuja idempotência
  justifica esta separação
- [ADR-020](adr-020-migracoes-por-modulo.md) — o gate que o seed herdou sem
  decisão
- [ADR-027](adr-027-app-service-em-vez-de-container-apps.md) — o ambiente onde
  a lacuna apareceu
