#!/usr/bin/env bash
#
# Nível 🟣 — envia as cópias para armazenamento externo compatível com S3.
#
# **Cifrado antes de sair**, e não pelo fornecedor. O arquivo leva o `.env`:
# chave de assinatura do JWT, password do SMTP, ligação à base de dados. Confiar
# a cifra a quem guarda o ficheiro é confiar-lhe também a chave — aqui a chave
# fica de fora e o balde só vê blocos ilegíveis, incluindo os nomes.
#
# Isso faz-se com um remoto `crypt` do rclone, que envolve outro remoto. Não é
# criptografia escrita à mão neste script, que seria a forma errada de resolver.
#
# Uso:
#   ./enviar-para-fora.sh
#
# Preparar uma vez (ver o README):
#   rclone config    # cria 'rivo-s3' (o balde) e 'rivo-cofre' (crypt sobre ele)
#
set -Eeuo pipefail

ORIGEM="${ORIGEM:-/var/backups/rivo}"
REMOTO="${REMOTO:-rivo-cofre:}"
SEMANAS_A_GUARDAR="${SEMANAS_A_GUARDAR:-13}"   # um trimestre

dizer() { printf '[%s] %s\n' "$(date -u +%H:%M:%S)" "$*"; }

command -v rclone >/dev/null || {
  echo "rclone não está instalado. Ver o README ao lado." >&2
  exit 2
}

# O remoto tem de existir E de ser do tipo crypt. Um `rivo-cofre` apontado
# directamente ao balde enviaria os segredos em claro, e ninguém daria por isso
# até ao dia em que alguém olhasse para o balde.
if ! rclone listremotes | grep -qx "${REMOTO}"; then
  echo "Remoto '${REMOTO}' não configurado. Corra: rclone config" >&2
  exit 2
fi

TIPO="$(rclone config show "${REMOTO%:}" | grep -E '^type *=' | head -1 | cut -d= -f2- | tr -d ' ')"
if [ "${TIPO}" != "crypt" ]; then
  echo "O remoto '${REMOTO}' é do tipo '${TIPO}', e não 'crypt'." >&2
  echo "O arquivo leva segredos: sem cifra, não sai desta máquina." >&2
  exit 3
fi

dizer "A enviar para ${REMOTO} (cifrado)..."

# `copy` e não `sync`: o destino externo é a última linha de defesa, e um `sync`
# propagaria para lá um apagão local. A rotação faz-se abaixo, por idade, e
# nunca por espelhar o que resta na origem.
rclone copy "${ORIGEM}" "${REMOTO}" \
  --include 'rivo-*.tar.gz' \
  --transfers 2 \
  --stats-one-line \
  --stats 30s

# ---------------------------------------------------------------------------
# Confirmar que chegou. Um envio que devolve zero e não deixa nada do outro lado
# é possível — balde errado, prefixo errado, quota cheia — e cala-se.
# ---------------------------------------------------------------------------
LOCAL_ULTIMO="$(ls -1t "${ORIGEM}"/rivo-*.tar.gz 2>/dev/null | head -1 || true)"
[ -n "${LOCAL_ULTIMO}" ] || { echo "Nada para enviar em ${ORIGEM}." >&2; exit 1; }

NOME="$(basename "${LOCAL_ULTIMO}")"
if ! rclone lsf "${REMOTO}" | grep -qx "${NOME}"; then
  echo "O arquivo mais recente (${NOME}) não aparece no destino. Envio falhou em silêncio." >&2
  exit 4
fi

TAM_LOCAL="$(stat -c%s "${LOCAL_ULTIMO}")"
TAM_REMOTO="$(rclone size "${REMOTO}${NOME}" --json 2>/dev/null | grep -oE '"bytes":[0-9]+' | cut -d: -f2 || echo 0)"

if [ "${TAM_LOCAL}" != "${TAM_REMOTO}" ]; then
  echo "Tamanhos diferentes para ${NOME}: local ${TAM_LOCAL}, remoto ${TAM_REMOTO}." >&2
  exit 5
fi

dizer "Confirmado: ${NOME} (${TAM_REMOTO} bytes) está no destino."

dizer "A rodar o que passou de ${SEMANAS_A_GUARDAR} semanas..."
rclone delete "${REMOTO}" --min-age "$((SEMANAS_A_GUARDAR * 7))d" --include 'rivo-*.tar.gz'

dizer "Pronto. $(rclone lsf "${REMOTO}" | wc -l) arquivo(s) no destino externo."
