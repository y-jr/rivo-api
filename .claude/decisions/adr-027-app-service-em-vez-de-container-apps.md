# ADR-027: App Service em vez de Container Apps

## Status

Aceite (2026-08-16)

## Context

O percurso de execução (Fase 1) escolheu **Azure Container Apps** como destino
da API, com esta justificação:

> Preferido a App Service porque o worker de entrega de notificações vive hoje
> no mesmo processo: Container Apps dá revisões, escala por regra e um caminho
> directo para o separar mais tarde sem mudar de plataforma. App Service
> serviria hoje e obrigaria a pagar a migração depois.

A justificação continua correcta. **O que mudou não foi a análise — foi uma
restrição que não era conhecida.**

Ao validar o template contra a subscrição real:

```
MaxNumberOfGlobalEnvironmentsInSubExceeded: The subscription
'1560a06e-...' cannot have more than 1 Container App Environments.
```

A subscrição é institucional (tenant do IADE, conta de aluno) e permite **um
só ambiente de Container Apps**. Esse ambiente já existe —
`cae-wikima-demo`, em France Central — e pertence a outro projecto do
utilizador.

Não é limite que se levante numa subscrição destas.

## Requirements

- **Facto** — A API é um contentor Linux, publicado num registo privado.
- **Facto** — O worker de entrega de `notifications` é um `BackgroundService`
  **no mesmo processo** da API. Se o processo for descarregado, a fila deixa
  de ser drenada.
- **Facto** — Os segredos vêm do Key Vault por identidade gerida, nunca de
  configuração (Fase 1).
- **Facto** — A subscrição permite um ambiente de Container Apps, já ocupado.
- **Hipótese** — A subscrição institucional é temporária. Uma plataforma real
  para um cliente em Angola não deve depender do acesso académico do autor.

## Alternatives

1. **App Service (Linux, contentor)** — escolhida.
2. Apagar `cae-wikima-demo` para libertar a quota.
3. Partilhar o ambiente de Container Apps existente.
4. Pedir aumento de quota.

A opção 2 resolve o problema técnico e mantém a escolha original, mas **destrói
trabalho de outro projecto** para acomodar este. Não é decisão que a
infraestrutura do Rivo deva tomar.

A opção 3 acopla dois projectos sem relação nenhuma no mesmo ambiente, e o
ambiente existente está em **France Central** enquanto o grupo de recursos do
Rivo está em **South Africa North** — um container app tem de viver na região
do seu ambiente. Perdia-se a proximidade a Angola *e* misturavam-se os
projectos.

A opção 4 não está disponível em subscrições gratuitas ou de estudante.

## Decision

**Azure App Service, plano Linux B1, a correr a imagem do ACR.**

- Identidade atribuída pelo sistema, que puxa a imagem (`AcrPull`), lê os
  segredos (`Key Vault Secrets User`) e escreve os anexos
  (`Storage Blob Data Contributor`). Nenhuma credencial em configuração — o
  `adminUserEnabled` do ACR fica desligado.
- Segredos por **referência ao Key Vault** nas app settings: o valor nunca
  passa pelo template nem fica legível na configuração do site.
- `healthCheckPath: /health`, que já existe.

### `alwaysOn: true` não é afinação — é requisito

Sem isto, o App Service descarrega a aplicação quando fica ociosa. Com ela iria
o worker de entrega de notificações, e a fila só voltaria a ser drenada quando
alguém fizesse um pedido HTTP. É a razão de o plano ser **B1** e não um escalão
gratuito: o gratuito não suporta `alwaysOn` nem contentores Linux.

### O que se perde face a Container Apps

Honestamente, e para não ser esquecido:

| Perde-se | Consequência |
|---|---|
| Revisões e tráfego repartido | Sem canary nem rollback instantâneo; o rollback é reimplantar a imagem anterior |
| Escala por regra (KEDA) | Escala por métricas do plano, mais grosseira |
| Caminho directo para separar o worker | Separar o worker passa a exigir um segundo recurso — Web App ou Function — e não apenas um segundo container app |
| Escala a zero | O `alwaysOn` mantém uma instância sempre paga |

**Nenhuma destas morde hoje.** São todas preocupações de quando houver carga
real, e a esta escala o custo de as adiar é próximo de zero.

## Consequences

Facilita:

- Desbloqueia a Fase 1 sem tocar em trabalho de outro projecto.
- Menos peças: sem ambiente gerido, o App Service é um recurso a menos a
  raciocinar.
- ~13 USD/mês previsíveis, em vez do consumo variável de Container Apps.

Dificulta / exige:

- Uma instância sempre a correr, mesmo sem tráfego.
- Quando o worker de `notifications` tiver de escalar independentemente da API,
  a migração acontece — que era exactamente o que a escolha original queria
  evitar. **A dívida é conhecida e datada, não escondida.**

## Risks

- **A subscrição é institucional.** Se o acesso académico terminar, a
  infraestrutura vai com ele. Vale para o App Service tanto como valeria para
  Container Apps, mas é o risco maior desta fase e não deve ficar por dizer.
  **Migrar para subscrição própria antes de haver dados reais.**
- **Decidir por restrição de ambiente é frágil.** Se amanhã a subscrição
  mudar, esta decisão deixa de ter fundamento — e o risco é que fique por
  inércia. Por isso o Revisit When abaixo é concreto.
- **B1 é um único nó.** Não há redundância; um reinício do plano é
  indisponibilidade. Aceitável em staging, não em produção.

## Revisit When

- A plataforma passar para uma subscrição própria — reabre imediatamente, e a
  justificação original de Container Apps volta a valer sem obstáculo.
- O worker de `notifications` precisar de escalar independentemente da API.
- Produção exigir mais do que um nó, ou implantação sem interrupção.

## Related

- [ADR-021](adr-021-ambiente-local-docker.md) — o ambiente local que esta
  espelha
- [state/roadmap-execucao.md](../state/roadmap-execucao.md) — Fase 1, cuja
  escolha esta decisão altera
- `infra/main.bicep`
