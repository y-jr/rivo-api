# Persistência

## Base de dados

Um PostgreSQL, **um schema lógico por domínio**, ownership exclusivo de
tabela (ADR-002).

Partilhar a instância não é partilhar ownership.

## Princípios de modelação

Directos de `docs/rivo-dados-integracoes-seguranca-v1.md` §1.1:

- **Um schema por domínio**, com ownership exclusivo. Nenhum domínio
  escreve directamente em tabelas de outro.
- **Sem `tenant_id`**, sem partição multi-tenant (ADR-003).
- **Chaves substitutas UUID** em todas as entidades. Evita expor sequências
  previsíveis e facilita eventual extracção futura.
- **Trilha de auditoria por referência** (`entidade_tipo` + `entidade_id`),
  nunca duplicada dentro de cada domínio. Todos escrevem para `audit`.
- **Concorrência optimista** (coluna `version` ou verificação de estado
  antes de escrever) em qualquer entidade decidida por mais do que uma
  pessoa — decisões de aprovação e execução de pagamento em particular
  (BR-17).
- **Sem eliminação física** em entidades sujeitas a auditoria ou retenção
  legal (decisões, pagamentos, documentos fiscais). Apenas anulação lógica,
  auditada.
- **Dados fiscais com vigência temporal** — taxas, escalões, limiares e
  códigos de isenção têm `vigente_desde`/`vigente_ate` e nunca são código
  (ADR-011). Registos revogados conservam-se; não se eliminam.

## Valores monetários

**`numeric`/`decimal` sempre.** Nunca vírgula flutuante para dinheiro,
taxas de imposto ou câmbio. Precisão explícita, definida ao modelar
`finance`.

## JSON

`audit` usa `jsonb` para os valores antes/depois.

## Chaves estrangeiras entre schemas

Regra de ADR-010:

- **Permitido:** FK exclusivamente para a **chave primária** do contexto
  dono (`fleet.viatura.motorista_id → hr.colaborador(id)`), só para
  integridade referencial.
- **Proibido:** FK para colunas que não sejam a chave primária; `JOIN` a
  outras tabelas do contexto dono; FK no sentido inverso ao da dependência
  declarada.
- Ler um atributo de outro contexto faz-se pelo **contrato publicado**, não
  por SQL.

Numa extracção futura, estas FKs degradam-se para identificadores simples.

## Ligação a documentos

Cada contexto consumidor possui a sua tabela de ligação, com FKs reais para
o seu registo e para `documents.documento(id)` (ADR-009). Não usar FK
polimórfica.

Excepção deliberada: `audit` mantém referência polimórfica sem FK, porque o
log tem de sobreviver à eliminação lógica do registo que descreve.

## Row-Level Security

RLS é usada para **segregação de funções**, não para isolamento de tenant
(não há tenants).

**Hierarquia obrigatória (ADR-008):**

1. O domínio é a fonte de verdade da regra.
2. RLS é segunda linha de defesa.
3. **Nenhuma regra de negócio pode existir apenas em RLS.** Toda a política
   RLS tem de reflectir uma invariante já expressa e testada no domínio.
   Uma regra que só existe em SQL é um defeito de arquitectura.

## Repositórios

- `Domain`/`Application` definem as interfaces em termos dos agregados do
  próprio módulo.
- `Infrastructure` implementa contra PostgreSQL. `Domain` nunca importa a
  tecnologia de persistência.
- Um repositório devolve e aceita agregados, não linhas nem DTOs.

## Migrações

Cada módulo evolui o seu schema de forma independente. As migrações devem
ser organizadas para que a migração de um módulo não obrigue a tocar
noutro.

PostgreSQL tem DDL transaccional — uma migração que falhe reverte
atomicamente. Não é desculpa para migrações grandes, mas elimina a classe de
falha "schema em estado parcial".

## Em aberto

ORM e tooling de migrações — ver
[architecture/technology-decisions.md](../architecture/technology-decisions.md).
