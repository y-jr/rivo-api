# Proxy da VPS

A configuração do reverse proxy que está à entrada de todo o sistema.
Versionada aqui desde 2026-09-12.

## Porque está no repositório de uma aplicação

Não está por posse. O ADR-031 fixa que o proxy é **infraestrutura
partilhada** — vive em `/opt/projects/proxy/` na máquina, numa rede `proxy`
externa, e o `docker-compose.yml` do Rivo nunca lhe toca.

Está por **recuperabilidade**. Até esta data existia uma cópia só, na VPS:
perder a máquina era perder a configuração de entrada do sistema, e nenhuma
alteração a ela passava por revisão. São duas coisas diferentes — quem é o
dono, e onde fica o que se pode reler.

Se um segundo serviço passar a viver atrás do mesmo proxy, isto muda-se para
um repositório próprio. Enquanto o Rivo for o único inquilino, a cópia fica
onde alguém a vai procurar.

## ⚠ A máquina é o que corre; isto é o que se lê

Não há nada que aplique este ficheiro automaticamente. O `deploy.yml` do Rivo
só toca em `/opt/projects/rivo/` — este directório fica de fora, de propósito,
porque reiniciar o proxy derruba tudo o que está atrás dele.

**É a mesma forma de divergência que já mordeu uma vez**: o `.env` vivo na VPS
não corresponde ao `.env.vps` deste repositório, e foi por isso que o
documento OpenAPI ficou exposto em produção sem ninguém reparar. Vale a pena
conferir de vez em quando:

```bash
ssh rivo@<vps> 'cat /opt/projects/proxy/Caddyfile' | diff - deploy/proxy/Caddyfile
```

Sem saída significa que estão iguais.

## Alterar

1. Editar aqui, e abrir PR — é isso que põe a alteração debaixo de revisão.
2. Copiar para a máquina:

   ```bash
   scp deploy/proxy/Caddyfile rivo@<vps>:/opt/projects/proxy/Caddyfile
   ```

3. Recarregar **sem derrubar** (o Caddy troca a configuração a quente):

   ```bash
   ssh rivo@<vps> 'docker exec -w /etc/caddy proxy-caddy caddy reload'
   ```

   `reload` e não `restart`: o segundo corta as ligações abertas, e o primeiro
   não.

4. Confirmar de fora, que é o único sítio onde o resultado conta:

   ```bash
   curl -sI https://syyt.tech/health | grep -i strict-transport
   curl -s -o /dev/null -w '%{http_code}\n' http://syyt.tech/health   # 308
   ```

## Estado em 2026-09-12

| | |
|---|---|
| Domínio | `syyt.tech` |
| Certificado | Let's Encrypt, renovação automática, válido até 02·12·2026 |
| HTTP | 308 para HTTPS, também no IP antigo |
| HSTS | `max-age=31536000`, sem `includeSubDomains` nem `preload` |
| K16 | **fechado** — era o único defeito marcado como impeditivo de produção |

## O que ainda não está aqui

O `docker network create proxy` continua a ser um passo manual de instalação
da máquina, documentado no ADR-031 e não neste directório. Fica registado
porque é o único pré-requisito que este compose não sabe criar sozinho: a rede
é declarada `external`, e sem ela o `up` falha.
