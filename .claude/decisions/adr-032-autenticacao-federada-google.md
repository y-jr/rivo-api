# ADR-032: Autenticação Federada com Google, por ID Token

## Status

Aceite (2026-08-22).

**Complementa o [ADR-012](adr-012-aspnet-core-identity.md) e o
[ADR-013](adr-013-jwt-bearer-e-sessao.md). Não substitui nenhum.** A password
continua a ser um caminho de autenticação de pleno direito; o Google passa a
ser um segundo.

## Context

O ADR-012 escolheu ASP.NET Core Identity e rejeitou provider externo **"por
agora"**, registando que o ADR-004 desenhou a autenticação precisamente para
ser substituível. `docs/rivo-arquitetura-global-v1.md` §D1 é explícito:

> Autenticação = infraestrutura (delegável a um provider, não é lógica de
> negócio)

Ou seja: federar a autenticação não contraria a fonte de verdade — é o caminho
que ela previu. O que faltava era a decisão de o percorrer, e o desenho de
*como*.

## Requirements

- **Facto** — `docs` §D1: autenticação é infraestrutura delegável.
- **Facto** — ADR-013: cada token de acesso está ligado a uma `Session`
  persistida, e é isso que o torna revogável.
- **Facto** — BR-12: tentativas de autenticação falhadas são registadas, não
  só bloqueadas.
- **Facto** — BR-9: a auditoria regista o IP de origem.
- **Facto** — ADR-016: ninguém nasce com autoridade; a criação de contas e a
  atribuição de perfis são actos deliberados.
- **Facto** — ADR-013 recusou cookies para não lidar com CSRF num frontend
  React desacoplado.
- **Facto** — `RivoIdentityDbContext` já mapeia `IdentityUserLogin<Guid>` para
  `identity.app_user_login`, e a tabela já existe na migração
  `20260820220852_InitialIdentity`.

## Constraints

- Não há multi-tenancy (ADR-003): um só domínio de organização a considerar.
- O `.env` da VPS é escrito à mão; qualquer variável nova obrigatória derruba
  o arranque até lá ser posta (ADR-031).
- O CI não tem credenciais da Google e nunca as terá.

## Alternatives

### Como a identidade chega ao Rivo

1. **ID token validado no servidor** (escolhida). O frontend obtém o ID token
   junto da Google e envia-o a `POST /identity/login/google`; o servidor valida
   a assinatura contra o JWKS da Google.
2. Redirect OAuth conduzido pelo backend (`AddGoogle`, challenge e callback).

A opção 2 é o caminho canónico do ASP.NET Core e foi rejeitada por duas razões
concretas, não por gosto:

- **Reintroduz cookies.** O fluxo de código de autorização precisa de cookies
  de correlação e de estado. O ADR-013 afastou cookies de propósito para não
  ter de lidar com CSRF num SPA desacoplado; voltar atrás por causa de um
  segundo método de login seria pagar esse preço todo outra vez.
- **Obriga a devolver o token ao SPA por redirect.** Na prática é um fragmento
  de URL, que fica no histórico do browser e em qualquer `Referer` mal
  configurado.

A opção 1 não precisa de `ClientSecret` — validar uma assinatura exige a chave
pública, que é o que o JWKS publica.

### O que fazer com uma identidade Google sem conta correspondente

1. **Recusar** (escolhida). O Google só serve para entrar em contas que já
   existem.
2. Criar a conta automaticamente, sem perfil.
3. Criar a conta, restrita ao domínio Google Workspace da organização.

As opções 2 e 3 tornam o login com Google um caminho de **criação** de contas.
Isso colide de frente com o ADR-016: a existência de uma conta no Rivo é acto
deliberado de quem administra, e não consequência de alguém ter carregado num
botão. A opção 2 deixa ainda qualquer titular de um Gmail escrever uma linha
em `identity.app_user`.

A opção 3 é defensável e fica registada como o próximo passo se a organização
adoptar Workspace com domínio próprio — a claim `hd` do ID token é exactamente
o que a torna implementável sem adivinhar.

## Trade-offs

| | Ganha | Perde |
|---|---|---|
| ID token (1) | Sem cookies, sem `ClientSecret`, sem CSRF | O frontend tem de integrar o SDK da Google |
| Redirect OAuth (2) | Caminho canónico, mais documentado | Cookies, CSRF e o token num fragmento de URL |
| Recusar conta nova (1) | ADR-016 intacto; superfície mínima | Um Admin tem de criar a conta antes |
| Auto-provisão (2, 3) | Menos um passo manual | O login passa a criar contas |

## Decision

**`POST /identity/login/google` recebe um ID token da Google, valida-o contra
o JWKS da Google, e — se corresponder a uma conta existente — emite o mesmo
JWT com sessão persistida que o login por password emite.**

### O Google autentica; a sessão continua a ser do Rivo

É a parte que não é negociável. O caminho do Google desagua exactamente no
mesmo `Session.Start()` e no mesmo `IAccessTokenIssuer` que o caminho da
password. Um token emitido à margem disso seria irrevogável, sem IP registado
e invisível para a auditoria — perder-se-iam de uma vez o ADR-013, o BR-9 e o
BR-12.

Para que os dois caminhos não divirjam com o tempo, o troço comum — criar
sessão, emitir token, auditar — passou a viver num sítio só, `SessionIssuer`.
Isto importa porque há requisitos por satisfazer que vão mexer nesse troço:
expiração por inactividade e sessão única reforçada. Duplicado, cada um deles
teria de ser implementado duas vezes, e a segunda seria esquecida.

### Validação exigida ao ID token

| Verificação | Valor |
|---|---|
| Assinatura | JWKS da Google, obtido do documento de descoberta OIDC e recarregado por rotação |
| `iss` | `https://accounts.google.com` ou `accounts.google.com` |
| `aud` | O `Google:ClientId` configurado |
| `exp` | Validado, com tolerância de 30 s |
| `email_verified` | Tem de ser verdadeiro |

**A tolerância de 30 s diverge do `ClockSkew = TimeSpan.Zero` do ADR-013, e é
deliberada.** No token do Rivo os dois relógios são o mesmo, e zero é a
escolha certa. O token da Google é emitido por um relógio que não controlamos:
zero tolerância transformaria uma deriva de segundos no relógio da VPS em
falhas de login intermitentes e sem explicação visível.

### `email_verified` é o que torna a ligação por e-mail segura

A primeira entrada liga a identidade Google à conta Rivo cujo e-mail coincide,
e essa ligação fica em `identity.app_user_login`. As entradas seguintes são
por `sub` da Google, que é estável e não muda se a pessoa mudar de e-mail.

Ligar por e-mail sem exigir `email_verified` seria uma via de tomada de conta:
bastaria registar, num provider que não verifica endereços, um e-mail igual ao
de alguém do Rivo. Com a verificação exigida, quem afirma o endereço é a
Google.

### O Google é opcional, e a sua ausência é explícita

Sem `Google:ClientId` configurado, o endpoint responde **501**, com a mesma
lógica com que `hr` responde 501 a um Cargo com autoridade: o sistema diz que
não faz, em vez de fingir que a credencial estava errada. Um 401 nesse caso
mandaria toda a gente procurar o defeito no sítio errado.

Não se usa `ValidateOnStart` aqui — torná-lo obrigatório derrubaria o arranque
em todos os ambientes de desenvolvimento e no CI, que não têm nem vão ter
credenciais da Google.

### Não há entidade de domínio nova

A ligação a um provider externo é infraestrutura de autenticação (`docs` §D1),
e o ASP.NET Core Identity já a modela em `app_user_login`. Criar um agregado
de domínio para ela seria acrescentar conceito ao domínio sem que nenhuma
regra de negócio o peça — o que [CLAUDE.md](../CLAUDE.md) proíbe.

**Consequência prática: não há migração nova.** A tabela já está no esquema.

## Consequences

**Mais fácil:** entrar sem gerir mais uma password. Para contas de decisor, a
credencial passa a estar sujeita às protecções da conta Google da organização.

**Mais difícil / exigido:**

- O frontend passa a integrar o SDK da Google e a lidar com dois caminhos de
  login.
- Há uma dependência de rede no caminho de autenticação: o JWKS da Google. É
  cacheado e recarregado por rotação, mas uma primeira validação com a Google
  inalcançável falha.
- A auditoria passa a registar o método usado (`password` ou `google`) em
  `new_value`. A acção mantém-se `identity.user.logged_in`, para que "todos os
  logins" continue a ser uma consulta só.

**Custo aceite:** o e-mail é o identificador de ligação na primeira entrada. É
mitigado por `email_verified`, e só vale na primeira — depois é o `sub`.

## Risks

- **MFA continua por implementar, e isto não o resolve.** A 2FA da conta
  Google é da Google, não do Rivo, e o Rivo não a consegue exigir nem
  verificar a partir do ID token. O requisito de MFA obrigatório para perfis
  com poder de aprovação (`modules/identity.md`) **continua por satisfazer** —
  e agora com uma aparência de resolvido que não tem. Registado como risco
  precisamente por isso.
- **`aud` mal configurado aceita tokens de outra aplicação.** Se o
  `Google:ClientId` estiver errado ou vazio, a validação de audiência perde o
  efeito. Por isso o caminho fica desligado quando o valor não está
  preenchido, em vez de validar sem audiência.
- **Uma conta desactivada no Google continua a entrar no Rivo** até a conta
  Rivo ser revogada. A federação não traz revogação em sentido inverso.
- **Ligação por e-mail depende de os e-mails coincidirem.** Uma conta Rivo
  criada com um endereço diferente do da Google não é encontrada, e o login é
  recusado com 401 — indistinguível, para o utilizador, de "não tens conta".

## Revisit When

- A organização adoptar Google Workspace com domínio próprio — aí a
  auto-provisão restrita por `hd` (alternativa 3) passa a fazer sentido.
- Houver um segundo provider federado — nessa altura o
  `IExternalIdentityVerifier` deixa de ser "o da Google" e passa a ser
  resolvido por provider.
- O MFA for implementado — é preciso decidir se o caminho federado o satisfaz,
  e a resposta hoje é não.

## Related

- [ADR-004](adr-004-identity-auth-vs-authz.md) — o split que tornou isto possível
- [ADR-012](adr-012-aspnet-core-identity.md) — ASP.NET Core Identity, e
  a rejeição de provider externo "por agora"
- [ADR-013](adr-013-jwt-bearer-e-sessao.md) — a sessão persistida em que este
  caminho desagua
- [ADR-014](adr-014-rbac-permissoes.md) — os perfis, que o Google não atribui
- [ADR-016](adr-016-bootstrap-autoridade.md) — porque é que o login não cria
  contas
