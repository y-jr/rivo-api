#!/usr/bin/env bash
#
# Nível 🔵 — puxa as cópias da VPS para o teu PC ou NAS.
#
# **Corre na tua máquina, não na VPS.** É essa a diferença que interessa: puxar
# em vez de empurrar significa que uma VPS comprometida não alcança o destino.
# Se fosse a VPS a enviar, teria de guardar uma credencial do teu NAS — e quem
# tomasse a VPS apagava as duas cópias.
#
# Uso:
#   ./puxar-para-local.sh
#   DESTINO=/mnt/nas/rivo ./puxar-para-local.sh
#
set -Eeuo pipefail

VPS="${VPS:-rivo@srv1924890}"
PORTA="${PORTA:-22}"
ORIGEM="${ORIGEM:-/var/backups/rivo/}"
DESTINO="${DESTINO:-${HOME}/backups/rivo}"
DIAS_A_GUARDAR="${DIAS_A_GUARDAR:-30}"

dizer() { printf '[%s] %s\n' "$(date -u +%H:%M:%S)" "$*"; }

mkdir -p "${DESTINO}"

dizer "A puxar de ${VPS}:${ORIGEM} ..."

# `--ignore-existing` e não `--update`: estes arquivos são imutáveis depois de
# escritos, por isso um que já cá esteja não precisa de ser comparado byte a
# byte — e a ligação da VPS é o recurso escasso, não o disco local.
rsync -avh --progress --ignore-existing \
  -e "ssh -p ${PORTA}" \
  "${VPS}:${ORIGEM}" "${DESTINO}/"

# ---------------------------------------------------------------------------
# Verificar o que chegou. Um `tar` truncado por uma ligação que caiu a meio
# parece um ficheiro normal até ao dia em que alguém o tenta abrir.
# ---------------------------------------------------------------------------
dizer "A verificar os arquivos locais..."
MAU=0
for f in "${DESTINO}"/rivo-*.tar.gz; do
  [ -e "${f}" ] || continue
  if ! tar -tzf "${f}" >/dev/null 2>&1; then
    echo "  CORROMPIDO: ${f}" >&2
    MAU=$((MAU + 1))
  fi
done

if [ "${MAU}" -gt 0 ]; then
  echo "${MAU} arquivo(s) ilegível(eis). Apague-os e volte a puxar." >&2
  exit 1
fi

ULTIMO="$(ls -1t "${DESTINO}"/rivo-*.tar.gz 2>/dev/null | head -1 || true)"
if [ -z "${ULTIMO}" ]; then
  echo "Nenhum arquivo em ${DESTINO}. A VPS está a produzir cópias?" >&2
  exit 1
fi

# ---------------------------------------------------------------------------
# A idade da cópia mais recente é o número que interessa: é o que se perde se a
# VPS desaparecer agora. Uma rotina silenciosamente parada há três semanas dá
# exactamente o mesmo aspecto de uma que corre todos os dias.
# ---------------------------------------------------------------------------
# `stat -c` é GNU, `stat -f` é BSD/macOS. Este script corre na máquina de quem
# guarda as cópias, que pode ser qualquer uma das duas — e falhar aqui calaria
# precisamente o aviso que se segue.
modificado_em() {
  stat -c%Y "$1" 2>/dev/null || stat -f%m "$1" 2>/dev/null
}

IDADE_H=$(( ( $(date +%s) - $(modificado_em "${ULTIMO}") ) / 3600 ))
dizer "Mais recente: $(basename "${ULTIMO}") — ${IDADE_H}h"

if [ "${IDADE_H}" -gt 48 ]; then
  echo "  ⚠ A cópia mais recente tem mais de 48 horas. A rotina da VPS parou?" >&2
fi

find "${DESTINO}" -maxdepth 1 -name 'rivo-*.tar.gz' -type f -mtime "+${DIAS_A_GUARDAR}" -print -delete \
  | sed 's/^/[rotacao] removido: /'

dizer "Pronto. $(ls -1 "${DESTINO}"/rivo-*.tar.gz | wc -l) arquivo(s) em ${DESTINO}"
