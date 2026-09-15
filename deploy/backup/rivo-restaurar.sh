#!/usr/bin/env bash
#
# Restauração do Rivo a partir de um arquivo produzido pelo `rivo-backup.sh`.
#
# **Este é o script que justifica o outro.** Uma cópia que nunca foi restaurada
# é uma suposição, não uma garantia — e a altura de descobrir que não presta não
# pode ser o dia em que é precisa. O README ao lado descreve o ensaio trimestral.
#
# Uso:
#   ./rivo-restaurar.sh /var/backups/rivo/rivo-20260915-030000.tar.gz
#   ./rivo-restaurar.sh <arquivo> --so-verificar    # lê e confere, sem escrever
#
set -Eeuo pipefail

PROJECTO="${PROJECTO:-/opt/projects/rivo}"
CONTENTOR_SQL="${CONTENTOR_SQL:-rivo-sqlserver}"
BASE="${BASE:-rivo}"
SQL_UTILIZADOR="${SQL_UTILIZADOR:-sa}"
BACKUP_NO_CONTENTOR="${BACKUP_NO_CONTENTOR:-/var/opt/mssql/backup}"
VOLUME_DOCUMENTOS="${VOLUME_DOCUMENTOS:-rivo_rivo-documents-data}"
CONTENTOR_API="${CONTENTOR_API:-rivo-api}"

ARQUIVO="${1:-}"
MODO="${2:-}"

if [ -z "${ARQUIVO}" ] || [ ! -f "${ARQUIVO}" ]; then
  echo "Uso: $0 <arquivo.tar.gz> [--so-verificar]" >&2
  exit 2
fi

TRABALHO="$(mktemp -d)"
trap 'rm -rf "${TRABALHO}"' EXIT

dizer() { printf '[%s] %s\n' "$(date -u +%H:%M:%S)" "$*"; }

dizer "A abrir ${ARQUIVO}..."
tar -xzf "${ARQUIVO}" -C "${TRABALHO}"

if [ ! -f "${TRABALHO}/${BASE}.bak" ]; then
  echo "O arquivo não tem ${BASE}.bak. Está incompleto ou não é do Rivo." >&2
  exit 3
fi

echo
sed 's/^/  /' "${TRABALHO}/MANIFESTO.txt" 2>/dev/null || echo "  (sem manifesto)"
echo

if [ -z "${SQL_PASSWORD:-}" ]; then
  if [ -f "${PROJECTO}/.env" ]; then
    SQL_PASSWORD="$(grep -E '^(BACKUP_SQL_PASSWORD|MSSQL_SA_PASSWORD|SA_PASSWORD)=' "${PROJECTO}/.env" \
      | head -1 | cut -d= -f2- || true)"
  fi
fi
[ -n "${SQL_PASSWORD:-}" ] || { echo "Sem password do SQL Server (defina SQL_PASSWORD)." >&2; exit 2; }

# Ver a nota em `rivo-backup.sh`: a password vai por variável de ambiente, e não
# por argumento, que apareceria em `ps`.
# Ver a nota em `rivo-backup.sh`: a saída é capturada e mostrada quando falha,
# porque o `sqlcmd` escreve os erros de SQL na saída normal — e num script de
# restauro, falhar sem dizer porquê é ainda pior do que num de cópia.
sqlcmd_no_contentor() {
  local saida
  if ! saida="$(docker exec -e "SQLCMDPASSWORD=${SQL_PASSWORD}" "${CONTENTOR_SQL}" \
      /opt/mssql-tools18/bin/sqlcmd \
      -S localhost -U "${SQL_UTILIZADOR}" -C -b -h -1 -W -Q "$1" 2>&1)"; then
    echo "--- o SQL Server respondeu ---" >&2
    echo "${saida}" >&2
    echo "------------------------------" >&2
    return 1
  fi

  if printf '%s' "${saida}" | grep -qiE '^(Msg [0-9]+|Sqlcmd: Error)'; then
    echo "--- o SQL Server respondeu ---" >&2
    echo "${saida}" >&2
    echo "------------------------------" >&2
    return 1
  fi

  printf '%s\n' "${saida}"
}

docker exec "${CONTENTOR_SQL}" mkdir -p "${BACKUP_NO_CONTENTOR}"
docker cp "${TRABALHO}/${BASE}.bak" "${CONTENTOR_SQL}:${BACKUP_NO_CONTENTOR}/restaurar.bak"

# ---------------------------------------------------------------------------
# Verificar sempre, mesmo quando se vai restaurar a seguir. É barato, e é o que
# distingue «o ficheiro existe» de «o ficheiro presta».
# ---------------------------------------------------------------------------
dizer "A verificar o ficheiro..."
sqlcmd_no_contentor "
RESTORE VERIFYONLY FROM DISK = N'${BACKUP_NO_CONTENTOR}/restaurar.bak' WITH CHECKSUM;" > /dev/null

dizer "A ler o cabeçalho:"
sqlcmd_no_contentor "
RESTORE HEADERONLY FROM DISK = N'${BACKUP_NO_CONTENTOR}/restaurar.bak';" \
  | head -5 | sed 's/^/  /'

if [ "${MODO}" = "--so-verificar" ]; then
  echo
  dizer "Só verificação. Nada foi escrito."
  docker exec "${CONTENTOR_SQL}" rm -f "${BACKUP_NO_CONTENTOR}/restaurar.bak" || true
  exit 0
fi

# ---------------------------------------------------------------------------
# Daqui para baixo escreve-se por cima do que existe. Confirmação explícita, e
# não uma bandeira `--sim` que se copia de um histórico de comandos sem ler.
# ---------------------------------------------------------------------------
cat <<AVISO

  ⚠  ISTO SUBSTITUI A BASE DE DADOS '${BASE}' E OS FICHEIROS CARREGADOS.
     Tudo o que exista agora e não esteja neste arquivo perde-se.
     A aplicação vai ser parada durante a operação.

AVISO
read -r -p "  Escreva o nome da base de dados para confirmar: " CONFIRMACAO
[ "${CONFIRMACAO}" = "${BASE}" ] || { echo "Não confirmado. Nada foi alterado."; exit 1; }

# A aplicação tem de sair primeiro: com ligações abertas, o SQL Server não
# consegue pôr a base em modo exclusivo, e o RESTORE falha a meio.
dizer "A parar a aplicação..."
docker stop "${CONTENTOR_API}" >/dev/null 2>&1 || dizer "(a aplicação já não estava a correr)"

dizer "A restaurar a base de dados..."
sqlcmd_no_contentor "
ALTER DATABASE [${BASE}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;" > /dev/null 2>&1 || true

# MOVE não é preciso: o backup vem da mesma imagem e os caminhos batem certo.
# REPLACE é o que autoriza escrever por cima de uma base com o mesmo nome.
sqlcmd_no_contentor "
RESTORE DATABASE [${BASE}]
FROM DISK = N'${BACKUP_NO_CONTENTOR}/restaurar.bak'
WITH REPLACE, RECOVERY, STATS = 25;" > /dev/null

sqlcmd_no_contentor "ALTER DATABASE [${BASE}] SET MULTI_USER;" > /dev/null

if [ -f "${TRABALHO}/documentos.tar.gz" ]; then
  dizer "A restaurar os documentos..."

  # `--delete` no sentido de ficar igual ao arquivo: um ficheiro que exista
  # agora e não conste da cópia é de depois dela, e não pertence ao estado que
  # se está a repor.
  docker run --rm \
    -v "${VOLUME_DOCUMENTOS}:/destino" \
    -v "${TRABALHO}:/origem:ro" \
    alpine:3 sh -c 'rm -rf /destino/* /destino/.[!.]* 2>/dev/null; tar -xzf /origem/documentos.tar.gz -C /destino'
fi

if [ -f "${TRABALHO}/env" ]; then
  dizer "O arquivo traz um .env. NÃO foi aplicado — veja ${TRABALHO}/env se precisar."
  dizer "Substituir configuração viva é decisão de quem opera, não deste script."
fi

dizer "A levantar a aplicação..."
docker start "${CONTENTOR_API}" >/dev/null

dizer "A esperar que responda..."

# Sondado de fora, num contentor descartável na mesma rede — e não com
# `docker exec` dentro da API. A imagem da aplicação é `aspnet`, que não traz
# `curl` nem `wget`: a sonda por dentro falharia sempre, e este script acabaria
# a dizer que a restauração correu mal quando tinha corrido bem.
REDE="${REDE:-proxy}"
sondar() {
  docker run --rm --network "${REDE}" curlimages/curl:8.11.1 \
    -sf --max-time 5 "http://${CONTENTOR_API}:8080/health" >/dev/null 2>&1
}

for _ in $(seq 1 60); do
  if sondar; then
    dizer "Restauração concluída, e a aplicação responde."
    exit 0
  fi
  sleep 3
done

echo "A aplicação não voltou a responder a tempo. Veja: docker logs ${CONTENTOR_API}" >&2
exit 4
