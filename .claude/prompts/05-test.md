# Prompt: Escrever Testes

Usar ao acrescentar ou melhorar cobertura de funcionalidade existente.
Testes escritos junto com uma funcionalidade nova seguem
[03-feature.md](03-feature.md).

## Passos

1. Identificar a camada onde vive a lógica (`Domain`, `Application`,
   `Infrastructure`, `API`) e aplicar a abordagem correspondente em
   [standards/testing.md](../standards/testing.md).

2. **Lógica de `Domain`:** teste sem infraestrutura, sem framework, sem base
   de dados. Se não for possível, **a lógica vazou do domínio** — assinalar
   isso em vez de forçar o teste com base de dados.

3. **Entre módulos:** testar o contrato publicado a partir do lado
   consumidor, com fakes. Nunca alcançando internals do fornecedor.

4. Asserir comportamento de negócio observável, não detalhes de
   implementação, para que o teste sobreviva a refactor.

5. Se o trabalho revelar lacuna nas regras documentadas
   ([modules/](../modules/) ou
   [domain/business-rules.md](../domain/business-rules.md)), actualizar essa
   documentação junto com o teste.

## Cenários que não podem faltar

Decorrem dos requisitos absorvidos do SGAP:

- Quem submete não consegue decidir sobre o próprio pedido (BR-2).
- Pagamento não executável sem decisão "Aprovado" registada (BR-1).
- Revalidação de estado **e** de saldo no momento da execução (BR-5).
- Aprovadores congelados na submissão não são recalculados por alteração
  organizacional posterior (BR-6).
- Duas pessoas a decidir em simultâneo — concorrência optimista (BR-17).
- Tentativa não autorizada fica registada em `audit`, não só bloqueada
  (BR-12).
- Anti-fraccionamento: despesas fraccionadas na janela de 30 dias são
  agregadas (BR-7).

## RLS

Verificar que as políticas RLS fazem o que dizem é teste de integração
válido — mas **em acréscimo** ao teste da invariante no domínio, nunca em
substituição (ADR-008).
