# Segurança

Segurança é parte da arquitectura, não uma camada acrescentada depois.

Destilado de `docs/rivo-dados-integracoes-seguranca-v1.md` §3.

## Pergunta orientadora

> O que deve ser **tecnicamente impossível** fazer, independentemente do que
> a interface permita?

Toda a verificação de autorização é feita no servidor/base de dados —
**nunca só na interface**. Isto corrige directamente o padrão de falha do
protótipo, onde a política de escrita em tabelas de aprovação era "qualquer
membro autenticado", com a verificação real só no frontend.

## Autenticação (infraestrutura, ADR-004)

- Autenticação individual. Sem contas partilhadas.
- **MFA obrigatório** para qualquer perfil com poder de aprovação ou de
  execução financeira.
- Password com política robusta e **hash forte** (bcrypt/argon2), nunca
  reversível.
- Sessões com expiração por inactividade (referência de partida: 15 min
  para perfis decisórios).
- Sessão única reforçada, configurável, para perfis decisórios sensíveis.

## Autorização (domínio partilhado, ADR-004)

Duas dimensões independentes, nunca confundidas (ADR-005):

| Dimensão | Responde a | Dono | Usada para |
|---|---|---|---|
| **Perfil de Acesso** | "O que este utilizador pode ver/fazer?" | `identity` | Visibilidade de módulos, permissões por ecrã |
| **Cargo** | "Que posição organizacional ocupa?" | `hr` | Resolução de aprovadores, segregação de funções |

## Segregação de funções

Regra de **código** no domínio `approval` — não configuração alterável por
administrador.

Mínimo garantido: quem submete um pedido nunca decide sobre ele.

Regras adicionais ("quem valida não aprova, quem aprova não paga") ficam
para o desenho detalhado, mas o **mecanismo de imposição já está decidido**:
código, não dados; servidor, não interface.

Uma pessoa pode ter vários perfis e cargos no sistema. O que não pode é
intervir mais do que uma vez, em papéis conflituantes, **no mesmo
processo** — verificado ao nível do Pedido de Aprovação, não do sistema
global.

**Relação com RLS (ADR-008):** o domínio é a fonte de verdade; a política
RLS é defesa em profundidade e tem de reflectir uma invariante já expressa
e testada no domínio. Uma regra que só exista em RLS é um defeito de
arquitectura.

A disponibilidade de RLS foi factor na escolha do motor: MySQL foi avaliado
e não a oferece, o que deixaria o domínio como única sede da invariante.
PostgreSQL foi mantido em parte por isso (ADR-002).

## Nota sobre a ausência de multi-tenancy

Sem isolamento multi-tenant (ADR-003), a primeira linha de defesa passa a
ser **inteiramente a autorização por perfil/cargo/processo** — já não há
fronteira de tenant a compensar eventuais falhas.

Isto reforça a necessidade de a segregação de funções ter também imposição
ao nível dos dados, nos termos acima.

## Protecção de dados

- **TLS** em toda a comunicação cliente-servidor e servidor-integrações.
- **Cifra em repouso (AES-256)** para dados sensíveis e anexos —
  em particular `documents` e campos financeiros/pessoais de `hr`.
- **Minimização e prazos de retenção** nos termos da Lei n.º 22/11
  (Protecção de Dados Pessoais, Angola), excepto onde a retenção legal
  obriga ao contrário (auditoria: mínimo 10 anos; documentos fiscais:
  prazos legais angolanos).
- **Sem eliminação física** de dados sujeitos a auditoria ou obrigação
  fiscal — apenas anulação lógica.

## Segurança de API

- Todos os endpoints exigem autenticação e autorização explícitas. **Nenhum
  endpoint aberto por omissão.**
- **Nunca expor entidades de domínio** — a API expõe DTOs/contratos
  próprios.
- **Rate limiting** por utilizador/IP, em particular em endpoints de
  autenticação (força bruta) e nos que desencadeiam integrações externas
  (amplificação de custo sobre terceiros).
- Validação de input em todos os endpoints, com mensagens de erro que não
  revelam detalhes internos.
- **Correlation ID** por pedido, propagado para logs e para `audit`.

## Segurança de integrações

- **Segredos** geridos por serviço de segredos ou variáveis de ambiente.
  Nunca em código nem em migrações.
- **Ambientes separados** (sandbox/produção) para integrações que o
  ofereçam.
- **Webhooks de entrada** validam a assinatura antes de qualquer
  processamento, e só desencadeiam efeitos de domínio pela interface
  interna do módulo responsável — nunca escrevem directamente em tabelas de
  negócio.
- **Circuit breaker** para integrações críticas: se o gateway de pagamento
  cair, o Portal do Cliente degrada graciosamente, não falha o resto da
  aplicação.

## Auditoria de segurança

- Toda a tentativa de acção **não autorizada** é registada explicitamente,
  não apenas bloqueada em silêncio.
- Alterações de configuração (perfis, regras de aprovação, parâmetros) são
  auditadas com a mesma disciplina que transacções de negócio.
- Usa a mesma capacidade `audit` — não uma tabela de segurança separada.

## Recuperação

RPO ≤ 24h, RTO ≤ 8h, testes de restauro semestrais (SGAP RNF-008). Âmbito
confirmado para Pagamentos; extensão a toda a plataforma é **hipótese** por
confirmar.
