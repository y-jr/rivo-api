# ADR-059: O convite substitui o registo público, e o correio passa a sair

## Status

Aceite (2026-09-13). **Alteração de contrato: `POST /identity/register` deixou
de existir.** Decisão do utilizador, depois de observar o efeito em produção:
«Eu criei uma conta e logo a seguir consegui acessar ao portal. Isso está
errado.»

## Context

Qualquer pessoa com o endereço da aplicação criava conta em
`POST /identity/register` e entrava. A conta nascia sem perfil — e isso estava
registado como comportamento correcto, «uma conta de `register` nasce sem poder
fazer nada» —, mas sem poder fazer nada **dentro** da aplicação: via o
esqueleto, os menus, e recebia 403 a cada chamada.

O argumento de que isso bastava tinha um furo que só se vê de fora: numa
ferramenta de gestão de uma empresa, **quem tem conta é quem a empresa decidiu
que tem**. Uma conta que existe é superfície — aparece em listagens, consome
tentativas de entrada, e um erro de atribuição de perfil passa a dar acesso
verdadeiro a um desconhecido.

Havia ainda uma segunda consequência, menos visível: criar a conta e atribuir o
perfil eram dois actos separados, e o segundo era fácil de esquecer. O sistema
enchia-se de contas autenticadas e inúteis.

### O que bloqueava fechar isto sem mais

`POST /identity/register` era **a única forma de criar uma conta**. Não havia
endpoint de administração para o fazer. Fechá-lo sozinho deixaria a aplicação
sem maneira nenhuma de dar entrada a alguém — por isso o convite tinha de
entrar na mesma alteração, e não depois.

E o convite precisa de correio que saia mesmo da aplicação. O canal registado
escrevia em log e não enviava nada (K13). Sem fornecedor de e-mail, o convite
não passava de uma entrada numa tabela.

## Requirements

- **Facto** — o registo público existia e dava entrada na aplicação a
  desconhecidos.
- **Facto** — não existia outra via para criar contas.
- **Facto (verificado)** — `notifications` não depende de módulo nenhum, e a
  notificação guarda `RecipientUserId` mas não o endereço. O canal de correio
  não podia perguntar a `identity` de dentro do módulo.
- **Decisão do utilizador** — caixa `geral@syyt.tech`, na Hostinger, que aloja
  também o domínio da API.

## Alternatives

1. **Manter o registo e exigir aprovação depois.** Rejeitada: a conta continua
   a nascer de quem chega, e o estado «à espera» é mais uma coisa para alguém
   esquecer.
2. **Manter o registo atrás de um código de convite partilhado.** Rejeitada:
   um segredo partilhado por toda a organização não identifica ninguém, e vaza
   uma vez para sempre.
3. **Administrador cria a conta e define a password inicial.** Rejeitada: quem
   define a password de outra pessoa pode entrar na conta dela. O ADR já
   separa `identity.users.write` de `roles.assign` precisamente para distinguir
   «o que pode fazer» de «quem é».
4. **Convite com testemunho por correio** (escolhida): a conta nasce sem
   password, e só quem recebe a mensagem no endereço escolhe a sua.

## Decision

### 1. O registo público sai

`POST /identity/register` deixou de existir, e com ele o caso de uso
`RegisterUser` e o DTO. Não ficou depreciado nem escondido: a rota devolve 404.

A constante de auditoria `identity.user.registered` fica, porque a trilha é
append-only (BR-14) e as entradas antigas continuam a referenciá-la.

### 2. Convidar é acto de quem administra contas

`POST /identity/invitations`, com `identity.users.write` — a mesma permissão de
repor passwords e desactivar contas, e pela mesma razão: decide **quem** alguém
é no sistema.

Cria a conta **sem password**, atribui-lhe o perfil, e enfileira a notificação
com a ligação. Sem password, a verificação de credenciais compara contra um
hash que não existe e falha sempre — a conta está reservada e ninguém lá entra.

**O perfil é obrigatório**: convidar alguém sem dizer para quê era o que
produzia as contas inúteis do registo. E **`SuperAdmin` não se convida** — de
outro modo, quem administra contornava por aqui a recusa de o atribuir
(ADR-058).

**Se a atribuição do perfil falhar, a conta fica desactivada e o convite não
sai.** Não há transacção que abranja as duas operações; desactivar é o mais
próximo de desfazer que o BR-14 permite.

### 3. O testemunho vai no correio, nunca na resposta

Quem convida recebe `{ userId }` e mais nada. Se o testemunho voltasse a quem
convida, convidar bastaria para entrar em nome de outra pessoa — que é
exactamente o que o convite existe para impedir.

O testemunho é o do próprio ASP.NET Core Identity, o mesmo da reposição de
password: uso único, com prazo, e já suportado pelos token providers
registados. Inventar um segundo mecanismo para o mesmo efeito era mais código e
mais superfície.

`POST /identity/invitations/acceptance` é público por necessidade — quem aceita
ainda não tem como se autenticar — e está na lista explícita de rotas públicas
verificada por `EndpointAuthorizationTests`. Tem tecto de pedidos, como as
outras públicas (ADR-058): um testemunho tentado à bruta é tão password como a
outra.

**Só serve uma conta que ainda não tem password.** Sem essa condição, um
convite antigo por consumir seria uma segunda via de reposição para uma conta
já em uso.

### 4. O correio sai por SMTP, e o canal vive na composição

`SmtpNotificationChannel` implementa `INotificationChannel` e substitui o canal
de log quando há servidor configurado. Vazio mantém o canal de log — a fila e o
worker continuam reais, o correio é que não sai.

**Vive em `Rivo.Api` e não em `notifications`**, e a razão é a fronteira: a
notificação guarda `RecipientUserId`, o endereço vive em `identity`, e
`ProjectReferenceTests` afirma `["Notifications"] = []`. Quem junta os dois é a
composição, sem que nenhum dos módulos passe a conhecer o outro. Para isso,
`identity` publica `IUserDirectory` — uma leitura estreita, com endereço e mais
nada, separada da porta interna do módulo (ADR-017).

**MailKit e não o `SmtpClient` do .NET**: a Hostinger documenta a porta 465 com
SSL implícito como principal, e o cliente embutido não a suporta — só faz
STARTTLS. É também a biblioteca que a Microsoft aponta desde que marcou o
`SmtpClient` como obsoleto para código novo.

## Consequences

- **Fecha o K13** no que interessa ao arranque: existe finalmente um canal que
  entrega fora da aplicação.
- O frontend perdeu a página de registo e o botão. No lugar do botão ficou uma
  frase que diz como se obtém acesso — sem ela, a pergunta seguinte era «então
  como é que eu entro?».
- **Convidar exige `Frontend:BaseUrl` configurado.** Sem ele, devolve 501: mais
  vale dizer que falta configuração do que enviar um convite que não abre.
- **E isso tornou-o requisito de qualquer ambiente**, não só dos que enviam
  correio. Convidar passou a ser a única via por que uma conta nasce, e as 21
  suites de `scripts/` montam as suas por lá — sem a variável, todas falham com
  501 antes de chegarem ao que verificam. Está no `.env` que o CI gera, no
  `.env.example` (já preenchido) e em `appsettings.Development.json`. ⚠ Este
  último **só vale para `dotnet run`**: sob docker compose,
  `Frontend__BaseUrl: ${FRONTEND_BASE_URL:-}` põe a variável vazia, e uma
  variável de ambiente vazia sobrepõe-se ao ficheiro.
- **As suites criam contas em dois passos, e não num.** O testemunho vai só para
  a caixa de correio, que nenhuma suite lê, por isso `New-RivoConta`
  (`_ambiente.ps1`) convida e depois fixa a password pela reposição de
  administrador. Substituiu 38 sítios que chamavam `register`.
- **Não há mais contas sem perfil, e nove suites usavam-nas** para provar 403.
  Passaram a usar `Cliente`, que tem exactamente uma permissão
  (`documents.write`): a verificação fica mais precisa, porque o 403 passa a
  provar que a distinção é por permissão e não por «ter ou não perfil».
- Em desenvolvimento, sem SMTP, a ligação **não aparece nos logs** — o canal de
  log não escreve o corpo, que pode levar o testemunho. Lê-se de
  `notifications.notification`.

## Risks

- **O frontend não vive atrás do mesmo proxy que a API**, e o convite aponta para
  ele. `syyt.tech` serve só a API (o Caddyfile encaminha tudo para
  `rivo-api:8080`); a aplicação está na Vercel, em
  `https://rivo-lac.vercel.app` — verificado a 2026-09-13, já com o registo
  fechado e a página `/convite` publicada. É esse o valor de
  `FRONTEND_BASE_URL` na VPS, e apontá-lo para `syyt.tech` daria convites cuja
  ligação responde 404.
- **A caixa `geral@syyt.tech` é lida por alguém.** Foi escolha deliberada face
  a um `nao-responder@`: uma resposta ao convite chega a uma pessoa. O custo é
  que o endereço de onde sai o correio automático é o mesmo que recebe correio
  humano.
- **Sem SMTP configurado em produção, convidar deixa de funcionar na prática** —
  a conta é criada, o convite fica na fila, e ninguém o recebe. É o mesmo modo
  de falha do K13, agora com consequência directa.
- O prazo do testemunho é o valor por omissão do Identity (um dia). Se se
  revelar curto para quem só lê o correio ao fim de semana, configura-se em
  `DataProtectionTokenProviderOptions` — não foi alterado aqui por não haver
  evidência de que o valor por omissão incomode.

## Revisit When

- Se aparecer necessidade de auto-registo controlado (um portal de clientes
  onde o próprio cliente se inscreve, por exemplo), isto tem de ser revisto —
  mas como caminho separado e com o seu próprio desenho, não reabrindo o
  registo geral.
- Se o fornecedor de correio mudar, muda a configuração e não o código: a porta
  `INotificationChannel` é a mesma.

## Related

ADR-016 (bootstrap do primeiro Admin — a outra via pela qual uma conta nasce
sem convite), ADR-058 (`SuperAdmin`, que não se convida), ADR-017 (contratos
publicados por módulo), ADR-041 (camada de composição), ADR-013 (sessão e JWT).
K13 em `known-issues.md`.
