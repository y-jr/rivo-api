# API

REST. A camada `API` é a mais fina: traduz transporte em casos de uso de
`Application` e resultados em respostas. **Sem lógica de negócio.**

## Contratos

- Pedidos e respostas são DTOs dedicados por endpoint. **Nunca entidades de
  domínio serializadas.**
- Cada módulo expõe a sua superfície sob o seu próprio namespace de rotas.
  Um módulo não faz proxy da API de outro.
- Os read models e canais (Dashboard, Portal do Colaborador, Portal do
  Cliente) são camadas de composição sobre as APIs dos módulos reais — não
  têm ownership de dados nem endpoints de escrita próprios que contornem o
  módulo dono.

## Idempotência

Obrigatória em qualquer operação que possa ser repetida — execução de
pagamento, submissão de aprovação, reenvio de notificação, reprocessamento
de exportação. Chave de idempotência por operação.

## Correlation ID

Todos os pedidos propagam um correlation ID para logs e para `audit`.
Permite reconstruir uma cadeia de acções entre módulos.

## Listagens

Paginação e filtragem explícitas em qualquer endpoint que devolva colecção.
Sem endpoints que devolvam tabelas inteiras.

## Versionamento

Alterações que quebrem um contrato existente exigem estratégia de
versionamento deliberada. Sem quebras silenciosas.

Mecanismo concreto por decidir — ver
[architecture/technology-decisions.md](../architecture/technology-decisions.md).

## Erros

Mapear violações de regra de domínio e erros de validação para códigos e
payloads consistentes, nos termos de
[error-handling.md](error-handling.md).

Falhas de infraestrutura mapeiam para resposta genérica — **detalhes
internos nunca são expostos ao cliente**.

## Autorização

Todos os endpoints exigem autenticação e autorização explícitas. Nenhum
endpoint é aberto por omissão. Ver [security.md](security.md).

## Documentação

OpenAPI por módulo.

## Integrações externas

Nunca bloquear um pedido HTTP à espera de uma integração externa. Todas
correm como background job, com retries e backoff. Ver
[architecture/architecture.md](../architecture/architecture.md).
