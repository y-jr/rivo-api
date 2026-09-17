# ADR-065: A recuperação de password responde sempre o mesmo

## Status

Aceite (2026-09-17). Pedido do utilizador: «active a opção de recuperação da
palavra passe por email (esqueceu a senha?)».

## Context

O botão **«Esqueceu a senha?»** existia no ecrã de entrada desde a maquete, e
estava desactivado. O comentário que o acompanhava dizia porquê:

> «A maquete liga isto a `/forgot-password`. O backend não tem endpoint de
> recuperação de password — nem de pedido, nem de reposição. Fica visível e
> inerte, com explicação, em vez de levar a um ecrã que não pode funcionar.»

Havia duas formas de alguém voltar a ter acesso, e nenhuma servia:

1. **`POST /identity/users/{id}/password-reset`** — quem administra define a
   password de outra pessoa. Exige `identity.users.write`, e passa a password
   pelas mãos de um terceiro.
2. **Um convite novo** — só funciona para contas sem password.

### O mecanismo já existia, montado para outra coisa

O convite (ADR-059) gera o testemunho com
`UserManager.GeneratePasswordResetTokenAsync` — **o token de reposição de
password**, usado para aceitar convites porque era o que estava à mão. Estava
tudo lá: token de uso único com prazo, notificação com `ActionUrl` desenhada
como botão, SMTP a funcionar, e uma página pública que consome o token.

Faltava a metade que não existia: a que começa em quem perdeu a password.

## Requirements

- **Facto** — `GeneratePasswordResetTokenAsync` e `ResetPasswordAsync` já estão
  em uso e são de uso único com prazo.
- **Facto** — a entrega de correio está provada desde 2026-09-15 (K13 fechado).
- **Facto** — o travão por cliente existe nas rotas públicas desde a correcção
  do K8.
- **Facto** — o SGAP impõe segregação de funções e auditoria append-only como
  requisitos vinculativos.
- **Inferência** — uma rota pública que diz se um endereço tem conta é um
  verificador de quem trabalha na empresa.

## Alternatives

1. **Dizer «não encontrámos esse endereço» quando não há conta.** Rejeitada, e é
   a alternativa que quase todo o software faz porque é mais simpática: dá a
   qualquer pessoa, sem autenticação, a resposta a «o João trabalha aqui?».
2. **Reutilizar `/invitations/acceptance` para a conclusão.** Rejeitada: aquele
   recusa contas que já tenham password, que é precisamente o caso normal aqui.
   Relaxar essa guarda tornaria um convite antigo por consumir numa segunda via
   de reposição para uma conta em uso.
3. **Exigir que quem administra faça a reposição.** É o que havia, e é o que se
   estava a substituir: obriga a pedir a um terceiro e põe a password nas mãos
   dele.
4. **Um par de rotas públicas que responde igual a tudo** (escolhida).

## Decision

### 1. Duas rotas públicas, com o mesmo travão do login

```
POST /identity/password-recovery              → 204, sempre
POST /identity/password-recovery/completion   → 204 | 400
```

Públicas por necessidade — e pela razão mais forte de todas: quem perdeu a
password não tem **nenhuma** forma de se autenticar. Ambas com
`RequireRateLimiting`: o pedido é um gerador de correio que não exige
autenticação (sem tecto, enche a caixa de alguém a partir de fora) e a conclusão
aceita testemunhos adivinhados como qualquer outra rota com segredo.

### 2. **204 para tudo** — é a decisão central

Endereço com conta, endereço desconhecido, conta desactivada: a resposta é a
mesma, sem corpo. A única diferença é o que acontece a seguir — sai correio num
caso e não nos outros —, e isso não é observável por quem pediu.

O ecrã acompanha: diz **«se existir uma conta com esse endereço, a mensagem já
saiu»**, e explica o que fazer quando nada chega (verificar a pasta de spam,
confirmar o endereço, falar com quem administra). Uma mensagem ambígua que
oriente é melhor do que uma precisa que informe quem não devia.

A excepção é o **corpo vazio**: um pedido sem endereço nenhum é recusado com 400,
porque não há endereço sobre o qual mentir, e um 204 esconderia um engano de quem
chama.

### 3. Uma conta desactivada não recupera acesso

Foi desactivada para deixar de entrar. Repor a password por correio seria uma
porta lateral para a reactivar sem que ninguém decidisse. Responde 204 como as
outras e não envia nada.

### 4. Uma conta **sem** password recupera normalmente

É o caso de quem perdeu o convite, e é melhor do que obrigar quem administra a
convidar outra vez. O testemunho vai para o endereço da conta, portanto quem o
recebe é quem sempre teria recebido o convite.

### 5. A trilha registra o que a resposta esconde

Três acções novas:

| Acção | Quando | `entity_id` |
|---|---|---|
| `password_recovery_requested` | sempre, com ou sem conta | o utilizador, ou **o endereço tentado** |
| `password_recovery_completed` | password mudada | o utilizador |
| `password_recovery_failed` | testemunho recusado | o utilizador |

O registo com o endereço tentado é a parte que interessa: é a única pista que
sobra de uma rota que responde sempre o mesmo, e uma sequência destes contra
endereços diferentes é exactamente o que se quer poder ver depois.

### 6. O testemunho vai no correio, nunca na resposta

Mesma disciplina do convite. Se voltasse a quem pediu, bastava pedir em nome de
outra pessoa para lhe entrar na conta. E não vai no **corpo** da mensagem: vai em
`ActionUrl`, que o canal de correio desenha como botão — um URL cru no texto
convida a copiá-lo para outro sítio.

O tipo `identity.password_recovery` entra na lista dos que **exigem entrega
externa**, ao lado do convite: enfileirá-lo com `SendEmail: false` rebenta em vez
de ficar calado na base de dados. Foi assim que o convite chegou a produção sem
sair, e o guarda existe por causa disso.

## Consequences

- **O botão do login deixou de estar inerte** — dois anos de maquete e três
  meses de produto depois, leva a algum lado.
- **`IUserAccounts` ganhou dois membros**, e o duplo dos testes com eles.
- **11 testes de aplicação novos** e `verify-authorization` de 15 casos para 23.
  O caso 18 é o que importa: verifica que o 204 do endereço inexistente é
  **byte a byte** o mesmo do endereço com conta, e que nada foi enviado.
- **Uma página nova no frontend** (`/recuperar-password` e `/recuperar`), fora de
  `RotaProtegida` — como a do convite, e pela mesma razão.

## Risks

- **A enumeração continua possível por temporização.** Uma conta existente faz
  mais trabalho (gerar token, enfileirar notificação) do que uma inexistente, e a
  diferença é medível com pedidos suficientes. O travão por cliente limita quantos
  se conseguem fazer, mas não elimina o canal. Resolver a sério exigiria trabalho
  constante nos dois caminhos, que não se faz sem medir primeiro.
- **A trilha guarda endereços tentados**, incluindo os de quem não tem conta.
  É informação pessoal de terceiros num registo append-only — deliberado, porque
  é a única forma de ver um ataque de enumeração, mas é um dado que não se apaga.
- **Nada limita quantas recuperações uma conta pede por dia** além do travão
  geral. Alguém que saiba o endereço de um colega pode encher-lhe a caixa de
  correio com mensagens legítimas do Rivo. Um tecto por conta seria melhor do
  que um tecto por cliente.
- **O prazo do testemunho é o do ASP.NET Identity** e não está configurado aqui
  — hoje são as omissões da framework. Devia ser explícito, e não é.

## Revisit When

- **Se MFA entrar** (é requisito do SGAP, ainda por fazer): recuperar a password
  passa a ter de considerar o segundo factor, senão torna-se a forma de o
  contornar.
- **Se houver observabilidade**: um pico de `password_recovery_requested` contra
  endereços diferentes é o sinal de enumeração, e hoje ninguém o vê.
- **Se a expiração do testemunho incomodar** — o valor é o da framework, e
  fixá-lo explicitamente é meia linha de configuração no dia em que se decidir
  qual deve ser.

## Related

ADR-059 (o convite, que trouxe o mecanismo do testemunho), ADR-013 (a sessão),
ADR-032 (entrada federada), K8 (o travão por cliente, que estas rotas usam),
K13 (a entrega de correio, sem a qual isto não funcionava).
