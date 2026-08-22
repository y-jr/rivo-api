# ADR-033: CORS por Configuração, sem Credenciais

## Status

Aceite (2026-08-22).

**Consequência directa do [ADR-013](adr-013-jwt-bearer-e-sessao.md).** Não
substitui nada.

## Context

O documento de produto fixa um frontend React separado, e o ADR-013 desenhou a
autenticação em torno disso — JWT bearer em vez de cookie, precisamente para
não lidar com CSRF num cliente desacoplado.

O que ficou por fazer foi a outra metade da mesma escolha: **um cliente
desacoplado corre noutra origem, e o browser bloqueia esses pedidos por
omissão.** Até agora não havia `AddCors` nem `UseCors` no host, o que torna a
API inutilizável a partir de qualquer SPA que não seja servido pelo mesmo
domínio.

Não é defeito de implementação de nada — é um requisito que nunca chegou a ser
decidido, porque até hoje não existia frontend.

## Requirements

- **Facto** — Frontend React separado (documento de produto, ADR-013).
- **Facto** — O token viaja em `Authorization: Bearer`, não em cookie
  (ADR-013).
- **Facto** — Em produção a API vive atrás de um reverse proxy e não publica
  porto no host (ADR-031).
- **Facto** — `docs` §3: nenhum endpoint aberto por omissão.
- **Inferência** — O domínio do frontend não é conhecido em tempo de
  compilação, e difere entre desenvolvimento, VPS e qualquer ambiente futuro.

## Constraints

- O `.env` da VPS é escrito à mão. Uma variável nova obrigatória derruba o
  arranque até lá ser posta (ADR-031).
- O CI não serve frontend nenhum e não pode depender desta configuração.

## Alternatives

1. **Origens por configuração, sem credenciais** (escolhida).
2. `AllowAnyOrigin`, com ou sem restrição por ambiente.
3. Não configurar CORS e servir o frontend do mesmo domínio, pelo proxy.

A opção 2 é a que aparece em todos os exemplos e é a que se rejeita primeiro:
qualquer página da Internet passaria a poder chamar a API a partir do browser
de quem lá estivesse autenticado. Sem cookies o estrago é menor do que
costuma ser — o token não viaja sozinho — mas continua a abrir a superfície a
quem não precisa dela, sem contrapartida nenhuma.

A opção 3 é legítima e continua disponível: um proxy que sirva o SPA e a API
no mesmo domínio dispensa CORS por completo. **Não se decide aqui qual das
duas topologias a VPS vai ter** — por isso a lista vazia tem de ser uma
configuração válida, e é.

## Trade-offs

| | Ganha | Perde |
|---|---|---|
| Por configuração (1) | Cada ambiente autoriza só quem deve | Uma variável a manter por ambiente |
| `AllowAnyOrigin` (2) | Zero configuração | Qualquer página do mundo chama a API pelo browser |
| Mesmo domínio (3) | CORS deixa de existir | Prende o frontend ao proxy da API |

## Decision

**Uma política, `browser-clients`, com as origens lidas de
`Cors:AllowedOrigins` — lista separada por vírgulas. Sem `AllowCredentials`.**

### Sem credenciais, e é decisão

`AllowCredentials` existe para que o browser envie cookies e cabeçalhos de
autenticação implícitos. O ADR-013 escolheu não usar cookies: o token vai no
`Authorization`, que o cliente põe explicitamente e que `AllowAnyHeader` já
cobre.

Ligar credenciais não acrescentaria capacidade nenhuma e proibiria para sempre
o uso de `*` em qualquer campo da política. **Se um dia se voltar a cookies,
isto muda — e vem CSRF atrás**, que é exactamente o que o ADR-013 evitou.

### Lista vazia é configuração válida

Sem origens, a política não autoriza ninguém. É o comportamento certo por
omissão e cobre a topologia em que o proxy serve o frontend e a API no mesmo
domínio.

Não se usa `ValidateOnStart`: exigir a variável derrubaria o arranque no CI e
em qualquer ambiente sem frontend. Em vez disso, **o arranque regista um aviso
quando a lista está vazia** — falhar em silêncio deixaria um frontend a dar
erros de rede sem causa visível, que é o pior modo de falha desta
configuração.

### A ordem no pipeline não é arbitrária

`UseCors` vem **antes** de `UseAuthentication`, e depois de
`UseForwardedHeaders`.

O pedido de verificação prévia que o browser envia é um `OPTIONS` **sem
cabeçalho `Authorization`**. Apanhado primeiro pela autenticação, seria
recusado com 401, e o pedido verdadeiro nunca chegaria a sair do browser — com
um erro que aponta para autenticação quando o problema é de ordem de
middleware.

### Vírgulas, e não array indexado

`Bootstrap:Users` usa índices porque cada entrada tem vários campos. Aqui é uma
lista de valores simples, e vive num `.env` escrito à mão:
`CORS_ALLOWED_ORIGINS=a,b` erra-se menos do que três linhas de
`Cors__AllowedOrigins__0`.

A barra final é retirada na leitura. O browser compara a origem por igualdade
textual, e `https://app.rivo.ao/` nunca casa com o `Origin: https://app.rivo.ao`
que ele próprio envia — um erro de configuração que não produz mensagem
nenhuma, apenas pedidos bloqueados.

## Consequences

**Mais fácil:** o frontend passa a poder correr em `localhost:5173` contra a
API local, sem proxy de desenvolvimento nem artifícios.

**Mais difícil / exigido:**

- Mais uma variável por ambiente. Esquecida na VPS, o frontend deixa de
  chamar a API — visível no aviso de arranque, não em silêncio.
- `WWW-Authenticate` é exposto ao JavaScript. Hoje não transporta nada; passa a
  transportar se a distinção entre sessão expirada e ausência de token vier a
  ser implementada.

## Risks

- **Uma origem a mais na lista é uma origem a mais com acesso.** A lista é o
  limite de confiança de clientes de browser, e cresce por conveniência se
  ninguém a revir.
- **CORS não é autorização.** Impede uma página noutra origem de ler a resposta
  pelo browser; não impede ninguém de chamar a API por `curl`. Toda a
  autorização continua a ser a do servidor (`docs` §3).

## Revisit When

- O frontend passar a ser servido pelo mesmo domínio que a API — aí a lista
  fica vazia e isto deixa de actuar.
- Se voltar a haver cookies de autenticação, o que obriga a `AllowCredentials`
  e traz CSRF.

## Related

- [ADR-013](adr-013-jwt-bearer-e-sessao.md) — bearer em vez de cookie, que é o
  que dispensa credenciais aqui
- [ADR-031](adr-031-deployment-em-vps.md) — o proxy à frente da API
- [ADR-018](adr-018-minimal-apis-e-routing.md) — a superfície HTTP que isto
  expõe a clientes de browser
