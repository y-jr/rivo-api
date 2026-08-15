# Problemas Conhecidos

_Última actualização: 2026-08-15._

Este ficheiro regista duas coisas distintas, e a distinção importa:

- **Lacunas de arquitectura (K1–K7)** — pontos assinalados em `docs/` e não
  resolvidos. Não são decisões em aberto; são buracos conhecidos no desenho.
- **Defeitos activos (K8–K14)** — comportamento real do código implementado
  que não satisfaz um requisito. Cinco módulos estão em produção de
  desenvolvimento, logo há defeitos de código a registar.

Os anti-padrões do protótipo ficam listados à parte, porque a tentação de os
repetir é real.

## Lacunas assinaladas em `docs/` e não resolvidas

| # | Lacuna | Impacto | Onde |
|---|---|---|---|
| K1 | Sobreposição entre Activos Fixos (`finance`, com depreciação) e Activos (`inventory`) | Nenhum dos módulos pode assumir ownership; bloqueia modelação de ambos | `docs` §1.2; [pending-decisions](pending-decisions.md) |
| K2 | Motor de cálculo fiscal não detalhado (taxas de IVA e incidência, escalões de IRT, taxas de INSS, mapeamento de códigos de isenção). O **modelo de dados** está fixado pelo XSD do SAF-T AO; as **regras de cálculo** não | Bloqueia `fiscal`, e por arrasto `commercial`, `procurement`, `payroll` | [modules/fiscal.md](../modules/fiscal.md) |
| K7 | Cadeia de `Hash`/`HashControl` do SAF-T implica assinatura ordenada e imutável dos documentos — requisito arquitectural ainda sem desenho | Afecta `commercial` e `finance`; tem impacto em concorrência e em ordenação de emissão | [modules/fiscal.md](../modules/fiscal.md) |
| K3 | Fluxo de despesa eventual avulsa do SGAP não coberto | Lacuna funcional — expansão de `procurement`, não módulo novo | `docs` §2 |
| K4 | Validação de conformidade documental antes da decisão (checklist DAF do SGAP) | Lacuna — `docs` aponta para expansão de `fiscal` como serviço de validação | `docs` §2 |
| K5 | Anti-fraccionamento (janela 30 dias) é regra nova, sem precedente no protótipo | Precisa de desenho — regra de `approval` alimentada por dados de `finance` | [domain/business-rules.md](../domain/business-rules.md) BR-7 |
| K6 | Disponibilidade de tesouraria ligada à execução não existia no protótipo | Conceito novo em `finance` | `docs` §2 |

## Anti-padrões do protótipo a não repetir

Registados porque a tentação de os repetir é real:

| # | Anti-padrão | Correcção |
|---|---|---|
| A1 | 5 implementações paralelas de aprovação | Motor único em `approval`; nenhum módulo tem passos de aprovação próprios |
| A2 | 2 tabelas de auditoria quase idênticas (`audit_logs`, `payroll_audit_logs`) | Capacidade única `audit` |
| A3 | Workflow de aprovação embutido em `payment_requests` | Pedido de Pagamento tem estado `elegível`/`executado`; a decisão vive em `approval` |
| A4 | Trigger que inseria até 20 notificações na mesma transacção da mudança de estado | Efeitos secundários fora da transacção de negócio |
| A5 | Storage de ficheiros reinventado por módulo (`file_url`, `pdf_path`, …) | Capacidade única `documents` (ADR-009) |
| A6 | `audit_logs` sem coluna de IP | `ip` obrigatório por desenho |
| A7 | RBAC com 4 papéis em código vs. 7 perfis no documento de produto | Catálogo único em `identity` |
| A8 | Política de escrita em tabelas de aprovação = "qualquer membro autenticado", verificação real só no frontend | Imposição no servidor/domínio (ADR-008) |
| A9 | `employees` sem FK para `auth.users` — "autenticado" e "colaborador" podiam não coincidir | Ligação explícita e opcional (ADR-004) |

## Defeitos activos

### K8 — IP da sessão é o do proxy, não o do cliente

- **Módulo:** `identity`
- **Impacto:** `HttpContext.Connection.RemoteIpAddress` devolve o endereço de
  quem estabelece a ligação TCP. Com a API em container, isso é o gateway da
  rede Docker (`::ffff:172.20.0.1`); atrás de um balanceador, será o
  balanceador. **O IP guardado em `user_session` não identifica o cliente**,
  o que esvazia o requisito de auditoria BR-9.
- **Contorno:** nenhum. Correr a API directamente no host regista o IP
  correcto, mas não é a topologia de produção.
- **Seguimento:** configurar `ForwardedHeadersMiddleware` para ler
  `X-Forwarded-For`. **Não é trivial:** aceitar esse cabeçalho sem restringir
  os proxies de confiança permite a qualquer cliente forjar o próprio IP —
  troca um registo inútil por um registo falsificável, que é pior. Exige
  saber a topologia de produção (há proxy? qual? que redes?), que ainda não
  está decidida.

### K9 — Garantia append-only da trilha não é imposta pela base de dados

- **Módulo:** `audit`
- **Impacto:** `AuditEvent` é imutável em código (sem setters públicos, sem
  métodos de alteração), mas nada impede um `UPDATE` ou `DELETE` directo em
  `audit.audit_event`. BR-10 exige append-only.
- **Contorno:** nenhum. A imutabilidade actual depende de a aplicação ser o
  único caminho de escrita.
- **Seguimento:** revogar `UPDATE`/`DELETE` na tabela para o utilizador
  aplicacional, com um papel separado para retenção. Depende da decisão sobre
  utilizadores de base de dados por módulo, que está em aberto.

### K10 — Escrita da trilha não é transaccional com a operação auditada

- **Módulo:** `audit` + consumidores
- **Impacto:** `audit` tem `DbContext` próprio, logo a escrita da trilha e a
  operação de negócio são transacções distintas. Se a segunda falhar depois
  de a primeira ter sido gravada, fica registada uma acção que não aconteceu;
  se falhar a escrita da trilha, a operação de negócio é abortada (as
  excepções propagam-se deliberadamente).
- **Contorno:** o comportamento actual erra do lado seguro — falha ruidosa em
  vez de perda silenciosa de auditoria.
- **Seguimento:** padrão outbox, se o volume ou a fiabilidade o exigirem.
  Não é necessário à escala actual.

### K11 — Documentos sem cifra em repouso

- **Módulo:** `documents`
- **Impacto:** `standards/security.md` exige **AES-256 em repouso** para
  anexos. O armazenamento em sistema de ficheiros guarda-os em claro. Um
  acesso ao volume lê contratos de trabalho e documentos fiscais.
- **Contorno:** nenhum ao nível da aplicação.
- **Seguimento:** normalmente resolve-se abaixo da aplicação — volume cifrado,
  ou cifra do lado do servidor no armazenamento de objectos. Cifrar na
  aplicação exigiria gestão de chaves, que é decisão pendente; criptografia
  com chave mal gerida seria pior do que esta ausência assinalada.
  **Depende da decisão sobre o serviço de armazenamento de produção.**

### K12 — Ficheiro órfão se a gravação de metadados falhar

- **Módulo:** `documents`
- **Impacto:** o conteúdo é escrito antes do registo. Se a gravação em base de
  dados falhar, fica um ficheiro sem metadados a apontar-lhe.
- **Contorno:** o modo de falha inverso — metadados a apontar para ficheiro
  inexistente — seria pior, e é por isso que a ordem é esta.
- **Seguimento:** limpeza periódica de ficheiros sem registo correspondente.
  Não urgente: o órfão ocupa espaço mas não corrompe nada.

### K13 — Notificações não são entregues fora da aplicação

- **Módulo:** `notifications`
- **Impacto:** o canal registado é `LoggingNotificationChannel`, que escreve
  uma linha de log e devolve. A fila, o worker, os estados e o recuo
  exponencial são reais; **o envio de e-mail não existe**. Uma notificação com
  `SendEmail = true` é marcada como entregue sem que ninguém a receba.
- **Contorno:** nenhum. É deliberado e está documentado no próprio código — o
  canal existe para que o percurso de entrega seja testável sem fornecedor.
- **Seguimento:** implementar `INotificationChannel` sobre o provider de
  e-mail transaccional e substituir o registo. **Depende da decisão de
  provider**, que está em aberto. Até lá, não confiar em notificação por
  e-mail para nada que tenha consequência — designadamente para pedidos de
  aprovação quando `approval` existir.

### K14 — Concorrência optimista exigida pelo ADR-002 não está implementada

- **Módulo:** todos os implementados
- **Impacto:** o ADR-002 fixa concorrência optimista por coluna `version`.
  **Nenhuma entidade declara token de concorrência.** Duas escritas
  simultâneas sobre o mesmo agregado sobrepõem-se em silêncio — a última a
  gravar ganha, sem erro.
- **Contorno:** nenhum ao nível do código. Na prática não morde ainda: nenhum
  dos cinco agregados implementados tem contenção real de escrita concorrente.
- **Seguimento:** **deixa de ser aceitável em `approval`.** BR-17 exige
  explicitamente concorrência optimista nas decisões, e é o cenário clássico —
  duas pessoas a decidir o mesmo pedido ao mesmo tempo. Implementar aí, e
  retroactivamente onde a contenção aparecer. Registado como desvio explícito
  em [ADR-019](../decisions/adr-019-persistencia-ef-core.md).

## Formato para defeitos futuros

```
## <título curto>
- Módulo: <módulo>
- Impacto: <o que falha ou está em risco>
- Contorno: <se houver>
- Seguimento: <o que tem de acontecer>
```
