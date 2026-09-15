#!/usr/bin/env bash
#
# Cópia de segurança do Rivo — nível 🟢, na própria VPS.
#
# Produz um arquivo por execução com três coisas: a base de dados, os ficheiros
# carregados, e a configuração do ambiente. É esse arquivo que os níveis 🔵 e 🟣
# levam para fora da máquina.
#
# ⚠ SOZINHO, ISTO NÃO É UM BACKUP. A base de dados e esta cópia vivem no mesmo
# host: protege contra migração má, apagão acidental e erro humano — todos reais,
# e um deles aconteceu — mas não contra perder a máquina. Só sai de cá com o
# `puxar-para-local.sh` (🔵) e o `enviar-para-fora.sh` (🟣).
#
# Uso:
#   ./rivo-backup.sh                 # usa os valores por omissão abaixo
#   DESTINO=/mnt/x ./rivo-backup.sh  # sobrepõe o que for preciso
#
set -Eeuo pipefail

# ---------------------------------------------------------------------------
# Configuração. Tudo sobreponível por ambiente, para isto não precisar de ser
# editado quando um nome mudar na máquina.
# ---------------------------------------------------------------------------
PROJECTO="${PROJECTO:-/opt/projects/rivo}"
DESTINO="${DESTINO:-/var/backups/rivo}"

CONTENTOR_SQL="${CONTENTOR_SQL:-rivo-sqlserver}"
BASE="${BASE:-rivo}"
SQL_UTILIZADOR="${SQL_UTILIZADOR:-sa}"

# Caminho DENTRO do contentor do SQL Server. Tem de ser um directório onde o
# processo do SQL Server saiba escrever — `/var/opt/mssql` é o volume dele.
BACKUP_NO_CONTENTOR="${BACKUP_NO_CONTENTOR:-/var/opt/mssql/backup}"

# ⚠ Com o prefixo do projecto. O `docker-compose.yml` declara
# `rivo-documents-data`, e o Compose grava-o como `<projecto>_<nome>` — aqui,
# `rivo_rivo-documents-data`. Confirmado na VPS a 2026-09-15; o nome sem prefixo
# não existe, e usá-lo daria um volume novo e vazio em vez de um erro.
VOLUME_DOCUMENTOS="${VOLUME_DOCUMENTOS:-rivo_rivo-documents-data}"
DIAS_A_GUARDAR="${DIAS_A_GUARDAR:-7}"

CARIMBO="$(date -u +%Y%m%d-%H%M%S)"
TRABALHO="$(mktemp -d)"
ARQUIVO="${DESTINO}/rivo-${CARIMBO}.tar.gz"

# ---------------------------------------------------------------------------
# Higiene. Um backup que falha a meio não pode deixar um arquivo truncado com
# ar de bom — é a pior falha possível nesta categoria, porque só se descobre no
# dia da restauração.
# ---------------------------------------------------------------------------
limpar() {
  local codigo=$?
  rm -rf "${TRABALHO}"
  docker exec "${CONTENTOR_SQL}" rm -f "${BACKUP_NO_CONTENTOR}/${BASE}.bak" 2>/dev/null || true

  if [ "${codigo}" -ne 0 ]; then
    rm -f "${ARQUIVO}"
    echo "FALHOU (codigo ${codigo}). O arquivo incompleto foi removido." >&2
  fi
  exit "${codigo}"
}
trap limpar EXIT

dizer() { printf '[%s] %s\n' "$(date -u +%H:%M:%S)" "$*"; }

# ---------------------------------------------------------------------------
# A password do SQL Server sai do .env do projecto, e não daqui. Nunca escrever
# credenciais num script versionado.
# ---------------------------------------------------------------------------
if [ -z "${SQL_PASSWORD:-}" ]; then
  if [ -f "${PROJECTO}/.env" ]; then
    # `SA_PASSWORD` é o nome que a imagem do SQL Server usa; `MSSQL_SA_PASSWORD`
    # é o mais recente. Aceita-se qualquer um, e também um dedicado ao backup.
    SQL_PASSWORD="$(grep -E '^(BACKUP_SQL_PASSWORD|MSSQL_SA_PASSWORD|SA_PASSWORD)=' "${PROJECTO}/.env" \
      | head -1 | cut -d= -f2- || true)"
  fi
fi

if [ -z "${SQL_PASSWORD:-}" ]; then
  echo "Sem password do SQL Server. Defina SQL_PASSWORD, ou ponha" >&2
  echo "BACKUP_SQL_PASSWORD no ${PROJECTO}/.env." >&2
  exit 2
fi

# ---------------------------------------------------------------------------
# Confirmar o que se vai copiar, antes de começar.
#
# **O `docker run -v` cria um volume vazio quando o nome não existe.** Um erro
# de nome não daria erro nenhum: daria um `documentos.tar.gz` com zero ficheiros
# dentro de um arquivo que parece bom — e só no dia da restauração é que alguém
# descobria que os contratos não estavam lá. Falhar aqui é barato; falhar lá é
# irrecuperável.
# ---------------------------------------------------------------------------
if ! docker ps --format '{{.Names}}' | grep -qx "${CONTENTOR_SQL}"; then
  echo "O contentor '${CONTENTOR_SQL}' não está a correr. A correr agora:" >&2
  docker ps --format '  {{.Names}}\t{{.Image}}' >&2
  exit 2
fi

if ! docker volume inspect "${VOLUME_DOCUMENTOS}" >/dev/null 2>&1; then
  echo "O volume '${VOLUME_DOCUMENTOS}' não existe. Volumes disponíveis:" >&2
  docker volume ls --format '  {{.Name}}' >&2
  echo "Sugestão: o Compose prefixa com o nome do projecto (rivo_...)." >&2
  exit 2
fi

mkdir -p "${DESTINO}"
mkdir -p "${TRABALHO}/arquivo"

# ---------------------------------------------------------------------------
# 1. Base de dados — backup nativo, e não exportação lógica.
#
# `BACKUP DATABASE` é consistente por construção: apanha um instante, com as
# transacções em curso resolvidas. Um `bcp` tabela a tabela apanharia cada uma
# num instante diferente, e o que voltasse podia não respeitar as chaves
# estrangeiras entre elas.
# ---------------------------------------------------------------------------
dizer "Base de dados '${BASE}' — a copiar..."

# A password vai por `SQLCMDPASSWORD` e não por `-P`. Um argumento de linha de
# comandos aparece em `ps` para qualquer utilizador da máquina; uma variável
# passada ao `docker exec` fica no processo do contentor.
sqlcmd_no_contentor() {
  docker exec -e "SQLCMDPASSWORD=${SQL_PASSWORD}" "${CONTENTOR_SQL}" \
    /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -U "${SQL_UTILIZADOR}" -C -b -h -1 -W -Q "$1"
}

docker exec "${CONTENTOR_SQL}" mkdir -p "${BACKUP_NO_CONTENTOR}"

# COPY_ONLY: não mexe na cadeia de backups diferenciais que o servidor possa vir
# a ter. CHECKSUM: o servidor calcula somas ao escrever, e é o que torna a
# verificação seguinte capaz de detectar corrupção em vez de só ilegibilidade.
sqlcmd_no_contentor "
BACKUP DATABASE [${BASE}]
TO DISK = N'${BACKUP_NO_CONTENTOR}/${BASE}.bak'
WITH INIT, COPY_ONLY, CHECKSUM, COMPRESSION, STATS = 25;" > /dev/null

# ---------------------------------------------------------------------------
# 2. Verificar antes de dar por bom.
#
# Um backup que nunca foi lido é uma suposição. `RESTORE VERIFYONLY` lê o
# ficheiro inteiro e confere as somas — não prova que os dados estão certos, mas
# prova que o ficheiro é restaurável, que é a falha mais comum e a mais cara.
# ---------------------------------------------------------------------------
dizer "A verificar o ficheiro..."
sqlcmd_no_contentor "
RESTORE VERIFYONLY
FROM DISK = N'${BACKUP_NO_CONTENTOR}/${BASE}.bak'
WITH CHECKSUM;" > /dev/null

docker cp "${CONTENTOR_SQL}:${BACKUP_NO_CONTENTOR}/${BASE}.bak" "${TRABALHO}/arquivo/${BASE}.bak"

# ---------------------------------------------------------------------------
# 3. Documentos carregados.
#
# São contratos de trabalho e comprovativos fiscais, e vivem num volume que até
# hoje não tinha cópia nenhuma (K12). A base de dados guarda os metadados; sem
# os ficheiros, as linhas em `documents.document` apontam para o vazio.
# ---------------------------------------------------------------------------
dizer "Documentos — a copiar o volume '${VOLUME_DOCUMENTOS}'..."

docker run --rm \
  -v "${VOLUME_DOCUMENTOS}:/origem:ro" \
  -v "${TRABALHO}/arquivo:/destino" \
  alpine:3 tar -czf /destino/documentos.tar.gz -C /origem .

# ---------------------------------------------------------------------------
# 4. Configuração do ambiente.
#
# ⚠ **Leva segredos**: chave de assinatura do JWT, password do SMTP, ligação à
# base de dados. É por isso que o nível 🟣 cifra antes de sair da máquina (ver
# `enviar-para-fora.sh`). Vai no arquivo porque sem ele a restauração é uma
# reconstrução: a aplicação sobe, e não sabe assinar sessões nem enviar correio.
# ---------------------------------------------------------------------------
if [ -f "${PROJECTO}/.env" ]; then
  cp "${PROJECTO}/.env" "${TRABALHO}/arquivo/env"
  dizer "Configuração incluída (leva segredos — ver o README)."
else
  dizer "AVISO: ${PROJECTO}/.env não encontrado. O arquivo vai sem configuração."
fi

# ---------------------------------------------------------------------------
# 5. Um manifesto, para quem restaurar saber o que tem nas mãos.
# ---------------------------------------------------------------------------
cat > "${TRABALHO}/arquivo/MANIFESTO.txt" <<FIM
Cópia de segurança do Rivo
==========================

Feita em      : ${CARIMBO} UTC
Máquina       : $(hostname)
Base de dados : ${BASE} (backup nativo, COPY_ONLY, com CHECKSUM verificado)
Documentos    : volume ${VOLUME_DOCUMENTOS}
Configuração  : $([ -f "${TRABALHO}/arquivo/env" ] && echo "incluída (SEGREDOS)" || echo "ausente")

Conteúdo
--------
  ${BASE}.bak        backup nativo do SQL Server
  documentos.tar.gz  ficheiros carregados pelos utilizadores
  env                .env do projecto, se existia

Para restaurar
--------------
  deploy/backup/rivo-restaurar.sh $(basename "${ARQUIVO}")

⚠ Restaurar substitui a base de dados e os ficheiros. O script pede confirmação.
FIM

dizer "A empacotar..."
tar -czf "${ARQUIVO}" -C "${TRABALHO}/arquivo" .

# ---------------------------------------------------------------------------
# 6. Rotação.
#
# O disco da VPS é pequeno, e um arquivo por dia enche-o sem avisar — o que
# derruba a aplicação por causa da rotina que existia para a proteger.
# ---------------------------------------------------------------------------
find "${DESTINO}" -maxdepth 1 -name 'rivo-*.tar.gz' -type f -mtime "+${DIAS_A_GUARDAR}" -print -delete \
  | sed 's/^/[rotacao] removido: /'

TAMANHO="$(du -h "${ARQUIVO}" | cut -f1)"
dizer "Pronto: ${ARQUIVO} (${TAMANHO})"
