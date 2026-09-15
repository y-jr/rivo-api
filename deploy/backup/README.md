# Cópias de segurança do Rivo

Os scripts que produzem, verificam, levam para fora e restauram as cópias.
Versionados aqui desde 2026-09-15.

## O que mudou, e porque isto passou a ser preciso

O **ADR-029** escolheu SQL Server com uma justificação explícita: o Rivo iria
correr *«contra uma instância de SQL Server já existente e já operada — a mesma
que serve outros sistemas da organização… **com backups, monitorização e um
administrador**»*.

Essa premissa **caiu**. A base de dados passou a ser um contentor na própria
VPS, e nada lhe faz cópia. O `known-issues.md` afirmava «a base de dados tem
backup» — era herdado daquela frase, e nunca tinha sido verificado.

Está registado no **ADR-060**.

## A estratégia, em três níveis

| | Onde | Quando | Contra o quê protege |
|---|---|---|---|
| 🟢 | VPS, `/var/backups/rivo` | diário | migração má, apagão acidental, erro humano |
| 🔵 | o teu PC ou NAS | diário | perder a VPS |
| 🟣 | balde S3-compatível, cifrado | diário | perder a VPS **e** o PC |

⚠ **O 🟢 sozinho não é um backup.** A base de dados e a cópia vivem no mesmo
host: se a máquina desaparece, desaparecem as duas. É o 🔵 que faz disto uma
cópia a sério, e o 🟣 que a torna sobrevivível a um incêndio em casa.

Isso não torna o 🟢 inútil — é ele que dá **recuperação em minutos** para o caso
mais provável de todos, que é alguém (ou alguma migração) estragar dados numa
base que continua de pé. Aconteceu esta semana.

## Os quatro scripts

```
rivo-backup.sh          🟢  produz o arquivo, na VPS
rivo-restaurar.sh       ↩   repõe a partir de um arquivo
puxar-para-local.sh     🔵  corre no TEU PC, puxa da VPS
enviar-para-fora.sh     🟣  corre na VPS, envia cifrado para o balde
```

### O que vai dentro do arquivo

| | |
|---|---|
| `rivo.bak` | backup **nativo** do SQL Server, não exportação lógica |
| `documentos.tar.gz` | o volume `rivo-documents-data` — contratos e comprovativos (K12) |
| `env` | o `.env` do projecto. **Leva segredos** |
| `MANIFESTO.txt` | o que é, de quando, e como se restaura |

**Backup nativo e não `bcp`**, e a razão é consistência: `BACKUP DATABASE` apanha
um instante único com as transacções resolvidas. Uma exportação tabela a tabela
apanharia cada uma num instante diferente, e o que voltasse podia não respeitar
as chaves estrangeiras entre elas.

**O `.env` vai lá dentro** porque sem ele a restauração é uma reconstrução: a
aplicação sobe e não sabe assinar sessões nem enviar correio. É também a razão
de o nível 🟣 cifrar — ver abaixo.

## Pôr a correr

### 🟢 Na VPS

```bash
sudo install -m 0755 rivo-backup.sh    /usr/local/bin/rivo-backup
sudo install -m 0755 rivo-restaurar.sh /usr/local/bin/rivo-restaurar
sudo mkdir -p /var/backups/rivo
```

Uma password dedicada ao backup, em vez do `sa` (ver abaixo), no
`/opt/projects/rivo/.env`:

```
BACKUP_SQL_PASSWORD=...
```

E no `crontab -e`:

```cron
# Cópia diária às 03:00. A saída vai para o log, senão um erro perde-se.
0 3 * * * /usr/local/bin/rivo-backup >> /var/log/rivo-backup.log 2>&1
```

### 🔵 No teu PC ou NAS

Este corre **do teu lado**, e é de propósito: **puxar em vez de empurrar**
significa que uma VPS comprometida não alcança o destino. Se fosse a VPS a
enviar, teria de guardar uma credencial do teu NAS — e quem tomasse a VPS
apagava as duas cópias de uma vez.

```bash
VPS=rivo@srv1924890 DESTINO=~/backups/rivo ./puxar-para-local.sh
```

Agendar: `cron` no Linux/macOS, Agendador de Tarefas no Windows.

### 🟣 Para o balde

Uma vez só, na VPS:

```bash
rclone config      # 1) 'rivo-s3'     — o balde (Backblaze B2, Wasabi, MinIO…)
                   # 2) 'rivo-cofre'  — tipo 'crypt', remoto = rivo-s3:rivo
```

O `crypt` é o que importa. **Cifra do lado de cá**, antes de sair da máquina,
incluindo os nomes dos ficheiros. Confiar a cifra a quem guarda o ficheiro seria
confiar-lhe também a chave.

⚠ **Guarda a palavra-passe do `rclone crypt` fora da VPS e fora do balde.**
Sem ela, as cópias externas são blocos ilegíveis — para ti também. Um gestor de
palavras-passe serve; o mesmo disco que estás a proteger não serve.

```cron
30 3 * * * /usr/local/bin/rivo-enviar-para-fora >> /var/log/rivo-backup.log 2>&1
```

## O ensaio de restauro

**Uma cópia que nunca foi restaurada é uma suposição.** A altura de descobrir
que não presta não pode ser o dia em que é precisa.

Cada script já verifica o que produz — `RESTORE VERIFYONLY` com `CHECKSUM` na
VPS, `tar -tzf` no PC, comparação de tamanho no balde. Isso apanha ficheiros
ilegíveis, e não apanha um backup que restaura para um estado errado.

Por isso, **de três em três meses**, e com o calendário a lembrar:

```bash
# 1. Só ler, sem escrever. Serve para qualquer arquivo, a qualquer momento.
rivo-restaurar /var/backups/rivo/rivo-ULTIMO.tar.gz --so-verificar

# 2. O ensaio a sério: restaurar para uma base descartável e olhar para dentro.
BASE=rivo_ensaio rivo-restaurar /var/backups/rivo/rivo-ULTIMO.tar.gz
```

O que confirmar no ensaio, e que um `VERIFYONLY` não diz:

- as contas em `identity.app_user` são as que esperas, e o `superadmin` lá está;
- `audit.audit_event` tem entradas até perto da hora da cópia;
- a contagem de `documents.document` bate com os ficheiros no arquivo;
- a aplicação **arranca** contra essa base e responde em `/health`.

Depois: `DROP DATABASE rivo_ensaio`.

## Duas coisas que isto não resolve

**O `sa`.** Estes scripts aceitam `BACKUP_SQL_PASSWORD` precisamente para não
dependerem dele, mas o utilizador restrito ainda não existe — continua no topo
da lista, ao lado disto. Um utilizador com `db_backupoperator` na base `rivo`
chega para o 🟢 e não serve para mais nada.

**Rollback de deploy.** São problemas vizinhos e não o mesmo: isto repõe dados,
não repõe a versão do código. Um deploy mau continua a substituir o contentor
sem retorno — está registado à parte.

## ⚠ A máquina é o que corre; isto é o que se lê

Como em `deploy/proxy/`, nada aplica estes ficheiros sozinho. O `main.yml` do
Rivo só toca em `/opt/projects/rivo/`, e instalar um script em
`/usr/local/bin/` é acto manual e deliberado.

Se alterares um destes ficheiros, **copia-o para a VPS e confirma que corre** —
senão fica a divergência de sempre: o repositório a descrever uma coisa e a
máquina a fazer outra. Já mordeu duas vezes.
