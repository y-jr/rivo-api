# ADR-060: A premissa de backup do ADR-029 caiu, e as cópias passam a ser nossas

## Status

Aceite (2026-09-15). Estratégia decidida pelo utilizador; implementação em
`deploy/backup/`.

## Context

O **ADR-029** trocou PostgreSQL por SQL Server, e a justificação não era
técnica — era de operação. Está escrita lá:

> O Rivo passa a correr numa VPS, contra uma instância de **SQL Server já
> existente e já operada** — a mesma que serve outros sistemas da organização.
> Não é uma preferência técnica: é a infraestrutura que existe, **com backups,
> monitorização e um administrador**.

E mais adiante, contra a alternativa de levantar uma instância própria:

> Levantar um PostgreSQL só para o Rivo significaria uma segunda tecnologia de
> dados para a mesma equipa manter, **com backups e retenção próprios**.

**Essa premissa deixou de valer.** A base de dados passou a ser um contentor na
própria VPS do Rivo, sem administrador de fora e sem rotina de cópia nenhuma. O
argumento que escolheu o motor — *alguém já trata disto* — não se aplica ao que
está a correr hoje.

A troca de instância não foi erro: deu ao projecto uma base que controla, e foi
o que permitiu limpar contas de teste sem pedir a ninguém. Só que **veio com o
custo que o ADR-029 tinha evitado**, e esse custo ficou por pagar.

### O que o projecto dizia de si próprio

O `known-issues.md`, ao descrever o K12, afirmava: «A base de dados tem backup;
o volume `rivo-documents-data` não tem.» Era herdado da frase do ADR-029 e
**nunca foi verificado**. Em 2026-09-15 não havia cópia nenhuma — nem da base,
nem dos documentos.

É a terceira vez esta semana que um registo do projecto descreve como resolvido
algo que nunca esteve: o K8 (cabeçalhos do proxy), o «canal de correio existe»
antes de alguma mensagem sair, e agora isto.

## Requirements

- **Facto** — O SQL Server corre em contentor na mesma VPS que a aplicação.
- **Facto** — Nada faz cópia da base de dados nem do volume de documentos.
- **Facto** — O volume `rivo-documents-data` guarda contratos de trabalho e
  comprovativos fiscais (K11, K12).
- **Facto** — O `.env` vivo na VPS guarda a chave de assinatura do JWT, a
  password do SMTP e a ligação à base. Sem ele, restaurar é reconstruir.
- **Decisão do utilizador** — três níveis: VPS diário, PC/NAS diário, externo
  S3-compatível diário ou semanal.

## Alternatives

1. **Voltar a uma instância operada por terceiros.** Rejeitada: o controlo da
   base é hoje uma vantagem real, e devolvê-lo para herdar backups seria trocar
   um problema resolúvel por uma dependência.
2. **Só cópia na VPS.** Rejeitada: base e cópia no mesmo host não sobrevivem à
   perda da máquina. Protege contra o erro mais **provável**, não contra o mais
   **grave**.
3. **Delegar no snapshot da VPS do fornecedor.** Rejeitada como cópia única: um
   snapshot de disco de uma base de dados a correr não é consistente por
   construção, e a restauração é da máquina inteira — não de uma tabela, nem de
   um dia. Serve de rede adicional, não de backup.
4. **Três níveis** (escolhida): 🟢 VPS, 🔵 PC/NAS, 🟣 externo cifrado. É a regra
   3-2-1, e cada nível protege contra a falha que o anterior não cobre.

## Decision

### 1. Backup nativo, e não exportação lógica

`BACKUP DATABASE ... WITH COPY_ONLY, CHECKSUM, COMPRESSION` dentro do
contentor. Um `bcp` tabela a tabela apanharia cada uma num instante diferente,
e o que voltasse podia não respeitar as chaves estrangeiras entre elas.

`COPY_ONLY` para não interferir com uma cadeia de diferenciais que venha a
existir; `CHECKSUM` porque é o que torna a verificação capaz de detectar
corrupção, e não apenas ilegibilidade.

### 2. Verificar sempre, e a verificação faz parte do backup

`RESTORE VERIFYONLY ... WITH CHECKSUM` corre **antes** de o arquivo ser dado
por bom, e um backup que falhe a meio é apagado em vez de ficar com ar de bom.
Do lado do PC, `tar -tzf`; do lado do balde, comparação de tamanho depois do
envio.

Nenhuma destas prova que os dados estão certos. Provam que o ficheiro é
restaurável — que é a falha mais comum e a que só se descobre tarde.

### 3. O `.env` vai no arquivo, e o nível externo cifra

Sem ele a aplicação sobe e não sabe assinar sessões nem enviar correio. Como
leva segredos, o nível 🟣 usa um remoto `crypt` do rclone: cifra deste lado,
antes de sair, incluindo os nomes dos ficheiros.

**Não se escreveu criptografia neste projecto para isto.** Envolver um remoto
noutro é o mecanismo que a ferramenta já tem, e é auditável por quem o conhece.

### 4. O nível 🔵 puxa, e não é empurrado

O script corre na máquina de destino. Se fosse a VPS a enviar, teria de guardar
uma credencial do NAS — e quem tomasse a VPS apagava as duas cópias. Puxar
inverte isso: a VPS não conhece o destino.

### 5. Restaurar é um script, e não um procedimento escrito

`rivo-restaurar.sh` existe porque **uma cópia nunca restaurada é uma
suposição**. Tem modo `--so-verificar`, que lê sem escrever, e o modo real pede
o nome da base escrito à mão — e não uma bandeira `--sim`, que se copia de um
histórico de comandos sem ler.

O README fixa um ensaio trimestral com o que confirmar **para além** do que o
`VERIFYONLY` diz: contas presentes, trilha até perto da hora da cópia,
documentos a bater com os metadados, e a aplicação a arrancar contra a base
restaurada.

## Consequences

- **O K12 fecha na parte da cópia** — os documentos passam a ser copiados. A
  cifra em repouso continua por fazer, e é outra coisa.
- **O `known-issues.md` tinha uma afirmação falsa**, e passa a dizer o que se
  verificou.
- **A retenção é uma decisão, e está escrita**: 7 dias na VPS, 30 no PC, 13
  semanas no externo. O disco da VPS é pequeno, e um arquivo por dia enche-o
  sem avisar — o que derrubaria a aplicação por causa da rotina criada para a
  proteger.
- **Nada disto corre sozinho a partir do repositório.** Como o `deploy/proxy/`,
  a máquina é o que corre e isto é o que se lê (ADR-031). Instalar é acto
  manual e deliberado.
- **O `sa` continua por resolver, e ficou mais visível.** Os scripts aceitam
  `BACKUP_SQL_PASSWORD` para não dependerem dele, mas o utilizador restrito não
  existe. Para o backup basta `db_backupoperator` na base `rivo`.

## Risks

- **A palavra-passe do `rclone crypt` é um ponto único de falha.** Perdê-la
  torna as cópias externas ilegíveis para o próprio dono. Tem de viver fora da
  VPS e fora do balde — um gestor de palavras-passe, não o disco que se está a
  proteger.
- **Uma rotina parada tem o mesmo aspecto de uma rotina a correr.** É por isso
  que o `puxar-para-local.sh` avisa quando a cópia mais recente passa das 48
  horas: é o único sinal automático que existe hoje, e é fraco. Resolve-se a
  sério com observabilidade (K-observabilidade), que continua por fazer.
- **O ensaio trimestral depende de alguém se lembrar.** Não há nada que o
  imponha. É a parte mais frágil deste ADR, e fica assumida como tal.

## Revisit When

- Se a base de dados voltar a ser operada por terceiros, o nível 🟢 passa a ser
  redundância e a retenção pode encurtar — mas os 🔵 e 🟣 continuam a valer, por
  serem de outra falha.
- Se o volume de documentos crescer ao ponto de o arquivo diário deixar de caber
  no disco ou na ligação, separa-se: base todos os dias, documentos por
  incremento.

## Related

ADR-029 (o motor, e a premissa que aqui cai), ADR-031 (a máquina é o que corre),
ADR-030 (migração no arranque, que é o que torna uma cópia anterior tão
importante), K11 e K12 em `known-issues.md`.
