# ADR-067: A sessão passa a ter dois prazos

## Status

Aceite — 2026-09-19. Implementado na mesma data.

Fecha a pendência **«Expiração por inactividade»** e a de **«Refresh token»** em
`state/pending-decisions.md`, a segunda por a deixar de ser necessária.

## Context

A sessão tinha **um** prazo: 60 minutos absolutos, sem deslizar. Isso dava o pior
dos dois mundos ao mesmo tempo.

**Expulsava quem estava a trabalhar.** À hora em ponto, a meio de um formulário,
sem aviso e sem forma de renovar — não há rota de renovação nem *refresh token*.
O utilizador perdia o que estava a preencher.

**E não protegia de quem tinha saído da secretária.** Uma sessão aberta e
abandonada continuava válida até ao fim da hora, exactamente como a de quem
estava a usá-la.

O requisito que faltava está nos documentos e é o oposto de «alargar»:
`docs/rivo-dados-integracoes-seguranca-v1.md` prevê **sessões com expiração por
inactividade**, dando 15 minutos para perfis decisórios como referência de
partida, e deixa expressamente em aberto se o limite deve ser uniforme ou por
perfil — decisão do cliente.

`state/pending-decisions.md` registava as duas coisas: a inactividade como ⚠ não
satisfeita, e o *refresh token* como «revisitar se a duração se revelar
incómoda». Revelou-se.

## Requirements

1. Trabalhar continuamente não expulsa ninguém.
2. Uma sessão parada morre, e mais depressa para quem tem autoridade de decisão.
3. Nenhuma sessão vive para sempre, mesmo com actividade.
4. O cliente pode avisar antes de expulsar alguém.
5. Não custa uma escrita na base de dados por pedido.
6. A escolha «uniforme ou por perfil» fica em configuração, não em código.

## Alternatives

| Alternativa | Porque não |
|---|---|
| **Aumentar o prazo absoluto** e mais nada | Resolve 1 e piora 2. Uma sessão de 12h abandonada é pior do que uma de 1h abandonada |
| **Refresh token** com token de acesso curto | Resolve 1, mas é maquinaria nova — credencial opaca, rotação, persistência, revogação própria — para uma garantia que a sessão persistida já dá. O token só vale enquanto a sessão valer, e a sessão é verificada a cada pedido desde o ADR-013 |
| **Expiração deslizante só, sem tecto** | Um cliente que faça um pedido por minuto mantinha uma sessão viva indefinidamente. Metade da protecção desaparecia |
| **Tolerância por nome de perfil** | Uma lista de nomes sobrevive mal: um perfil renomeado sai da lista em silêncio, e um perfil novo com autoridade de decisão não entra nela |
| **Escrever `last_seen_at` a cada pedido** | Uma escrita por cada leitura de página. O `pending-decisions` já antecipava o problema: «exige escrita por pedido ou estratégia de janela» |

## Decision

### 1. Dois prazos, e a sessão morre no primeiro

- **`ExpiresAt`** — o tecto absoluto. Longo (12h por omissão), porque não há
  razão para interromper quem está a trabalhar. Não desliza.
- **`IdleDeadline`** = `LastSeenAt + IdleTimeout` — desliza a cada pedido.

Trocar um pelo outro perderia metade da protecção, nas duas direcções. Ver a nota
na classe `Session`.

### 2. A tolerância depende das permissões, não do nome do perfil

`ISessionPolicy.IdleTimeoutFor(permissions)`. Quem tem **`approval.requests.decide`**
recebe o limite curto (15 min por omissão); os restantes o geral (30 min).

Por permissão e não por perfil porque sobrevive a renomear um perfil e a um perfil
novo que ganhe autoridade de decisão sem ninguém se lembrar de o acrescentar a uma
lista. E a própria lista de permissões decisórias é configuração
(`Session:DecisionPermissions`), para a conversa com o cliente sobre quem conta
como decisor não exigir uma alteração de código.

Se o limite decisório for configurado **acima** do geral, vale o menor: a intenção
era apertar, e alargar em silêncio para quem decide pagamentos seria o pior sítio
onde falhar.

### 3. A tolerância congela na sessão

`Session.IdleTimeoutSeconds` é resolvido no login e guardado. Baixar o limite na
configuração não mata sessões já abertas de surpresa, e subi-lo não prolonga
retroactivamente as que deviam morrer. A verificação por pedido fica também sem
depender de I/O de configuração.

### 4. A marca de actividade é escrita condicional, agrupada por janela

`OnTokenValidated` já lia a sessão a cada pedido autenticado desde o ADR-013.
Acrescenta-se-lhe uma escrita **condicional**:

```sql
UPDATE identity.user_session SET last_seen_at = @agora
WHERE id = @id AND last_seen_at < @agora - @janela
```

Com a janela a 60 segundos, escreve-se no máximo uma vez por minuto e por sessão.
O erro na conta da inactividade é no máximo um minuto, e é **sempre a favor da
segurança**: a marca fica atrasada, nunca adiantada.

**Fora do rastreio do EF e sem tocar no `version`**, e isso não é optimização: se
passasse pelo caminho normal, dois pedidos em paralelo do mesmo utilizador
colidiriam no contador de concorrência e um deles falharia. Perder uma marca de
actividade não é conflito nenhum — o outro pedido já a escreveu. A condição da
janela vai na instrução, para duas chamadas simultâneas se resolverem na base de
dados em vez de ambas decidirem que sim.

### 5. O token expira no tecto absoluto

E não no prazo de inactividade. A inactividade é verificada no servidor a cada
pedido, e um token curto obrigaria a um mecanismo de renovação para dar a mesma
garantia — mais peças para o mesmo resultado. O token continua a ser um portador
revogável pela sessão, que é o desenho do ADR-013.

**Consequência aceite:** um token roubado é portador por até 12h em vez de 1h. É
revogável de imediato (`/identity/me/sessions/{id}/revocation`, e a revogação em
massa que a desactivação de conta já dispara), e a inactividade não o limita —
quem o usa mantém a sessão viva. O que o limita é o tecto. Ver Risks.

### 6. O cliente recebe a janela

`LoginResponse` ganha `idleTimeoutSeconds`; `SessionView` ganha `lastSeenAt` e
`effectiveExpiry`.

Sem isto o cliente só conhece o prazo absoluto e não tem como avisar antes de
expulsar alguém — e a inactividade passa a ser a causa mais comum de fim de
sessão, e é a única que o utilizador pode evitar. Avisar é o que separa «a sessão
expirou» de «perdi o que estava a escrever».

### 7. As três causas de fim são distinguíveis

`SessionEndReason` — `Idle`, `Expired`, `Revoked` — e a mensagem do `401` difere.
«Terminou por inactividade» é accionável; «foi terminada» é outra conversa, e pode
querer dizer que alguém desactivou a conta.

### 8. A chave de configuração antiga faz o arranque falhar

`Jwt:SessionLifetimeMinutes` mandava na duração e está no `docker-compose.yml`.
Passou a `Session:AbsoluteLifetimeMinutes`.

Se a chave antiga estiver presente, **a API recusa arrancar** com uma mensagem a
dizer o que mudou. Ignorá-la em silêncio deixaria quem a configurou convencido de
que continua a mandar na duração — e a ter 12 horas onde pediu uma.

### 9. A migração preenche as colunas novas com valores utilizáveis

O EF gerou `0` segundos de tolerância e `0001-01-01` de última actividade, o que
**matava todas as sessões abertas no instante do deploy** e deixava uma coluna sem
sentido. Substituídos por 1800 segundos e `SYSDATETIMEOFFSET()` — «não sabemos
quando esta sessão foi usada, portanto conta a partir de agora». A generosidade é
limitada: nenhuma sessão anterior vive mais de 60 minutos, porque era esse o tecto
antigo, e esse tecto continua a valer para as que já existem.

## Consequences

- Quem trabalha continuamente deixa de ser expulso à hora. Passa a ser expulso
  uma vez por 12 horas, no tecto — e isso é um prazo deliberado, não um acidente.
- Quem para é desligado em 30 minutos, ou 15 se tem autoridade de decisão. **É
  mais restritivo do que antes**, e é o requisito que faltava.
- `ISessionPolicy` substitui `IAccessTokenIssuer.SessionLifetime`: quanto tempo
  uma autorização dura é regra, não formato de token.
- 26 testes novos. `verify-authorization` de 24 para 28 casos — incluindo um que
  espera 65 segundos para provar que o prazo desliza de facto, com a escrita na
  base de dados pelo meio. É o único que prova a coisa toda ligada.
- O *refresh token* deixa de ser necessário e sai das pendências com essa razão
  escrita.

## Risks

- **O token é portador por até 12 horas.** Revogável de imediato, mas quem o
  roubar e o usar mantém a sessão viva: a inactividade não protege contra uso
  activo. Quem quiser encurtar a janela baixa `AbsoluteLifetimeMinutes` — ou
  volta à conversa do *refresh token*, que este ADR fecha por desnecessário e não
  por errado.
- **Os valores são a referência dos documentos, não uma decisão do cliente.** 30 e
  15 minutos, e «decisor = quem decide aprovações». A pergunta «uniforme ou por
  perfil?» continua em `pending-decisions`; o que mudou é que responder-lhe passou
  a ser configuração.
- **A precisão da inactividade é de um minuto**, por causa da janela de
  agrupamento. Irrelevante para 15 ou 30 minutos; deixaria de ser se alguém
  configurasse um limite de 2 minutos.
- **`MFA` e «sessão única reforçada» continuam por fazer.** São os outros dois
  requisitos de sessão do SGAP e este ADR não lhes toca.

## Revisit When

- O cliente decidir os valores, ou decidir que o limite deve ser por perfil e não
  por permissão.
- A janela de 12h do portador se revelar larga demais para uma auditoria — aí o
  *refresh token* volta à mesa, agora com um problema concreto que resolve.
- «Sessão única reforçada» for implementada: mexe no mesmo sítio
  (`SessionIssuer`), que existe precisamente por isso.

## Related

- **ADR-013** — sessão persistida e token revogável. Este ADR estende-a sem
  mudar o desenho.
- **ADR-002, ADR-025** — concorrência optimista, e a razão pela qual a marca de
  actividade não passa por ela.
- **ADR-014** — permissões como *claims*, o que torna possível resolver a
  tolerância por permissão.
- **ADR-032** — login federado; passa pelo mesmo `SessionIssuer` e recebe a mesma
  política sem alteração própria.
- `docs/rivo-dados-integracoes-seguranca-v1.md` — o requisito de inactividade e a
  referência dos 15 minutos.
