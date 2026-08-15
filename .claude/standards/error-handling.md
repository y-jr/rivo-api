# Tratamento de Erros

## Categorias

### Violação de regra de domínio

Operação inválida segundo as regras de negócio — "não se pode aprovar um
pedido já rejeitado", "não se pode executar pagamento sem decisão
registada".

São **resultados esperados** que o chamador tem de tratar, não crashes.
Representar explicitamente (tipo Result/outcome, ou tipo de excepção de
domínio distinto de falhas técnicas) para que `Application` e `API` as
traduzam numa resposta com significado.

### Falha de infraestrutura

Base de dados indisponível, timeout de serviço externo. São genuinamente
excepcionais e pertencem a `Infrastructure`. Não devem ser apanhadas e
silenciadas por `Domain` ou `Application`.

### Erro de validação de input

Pedido malformado na fronteira da API. Rejeitar antes de chegar a
`Application`/`Domain`.

### Tentativa não autorizada

Categoria própria, por requisito de segurança: **é registada
explicitamente** em `audit`, não apenas bloqueada em silêncio (BR-12).

## Regras

- `Domain` não apanha nem referencia excepções de infraestrutura — nunca
  sabe que ela existe.
- Não acrescentar tratamento de erro para cenários que não podem ocorrer
  dadas as garantias do próprio sistema. Validar nas fronteiras, confiar
  nos contratos internos.
- Um erro que atravessa uma fronteira de módulo faz parte do contrato
  público desse módulo. Definir deliberadamente — não deixar vazar um tipo
  de excepção interno.
- Falhas com significado de negócio (um pagamento que falha na execução)
  são modeladas como factos/eventos de domínio, não apenas registadas em
  log.
- Mensagens de erro devolvidas ao cliente não revelam detalhes internos do
  sistema.

## Concorrência

Conflitos de concorrência optimista (BR-17) são violação de regra de
domínio, não falha técnica. Devem produzir uma resposta que o chamador
possa tratar — tipicamente "o registo foi alterado entretanto, recarregue".

Pertinente em particular a decisões de aprovação e execução de pagamento,
onde duas pessoas podem agir em simultâneo.

## Em aberto

Convenções concretas de excepção vs. tipo Result em C# — ver
[architecture/technology-decisions.md](../architecture/technology-decisions.md).
