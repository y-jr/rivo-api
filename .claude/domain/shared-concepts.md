# Conceitos Partilhados (SharedKernel)

## Princípio

Manter o SharedKernel mínimo. Um conceito só entra aqui se for
**estruturalmente idêntico** em todos os contextos que o usam e não tiver
dono de negócio natural.

Ser usado por vários módulos **não** é justificação. Quase todos os
conceitos de negócio do Rivo têm dono — ver
[domain-map.md](domain-map.md).

## O que NÃO pertence aqui

O erro que o protótipo cometeu foi não dar dono a conceitos recorrentes,
não a falta de um kernel partilhado. Nenhum destes entra no SharedKernel:

| Conceito | Dono real |
|---|---|
| Colaborador | `hr` — acede-se pelo contrato `ReferenciaColaborador` (ADR-010) |
| Fornecedor | `procurement` |
| Cliente | `commercial` |
| Cargo, Departamento | `hr` |
| Centro de Custo | `finance` |
| Aprovação | `approval` |
| Documento | `documents` |
| Auditoria | `audit` |

`Colaborador` é o caso mais tentador — tem o maior fan-out do sistema e
parece um kernel partilhado. **Não é.** É uma entidade de `hr` exposta por
contrato estreito. Tratá-la como shared kernel dissolveria a fronteira mais
importante do sistema.

## Conteúdo actual

_(vazio — nada foi ainda justificado para o SharedKernel)_

## Candidatos plausíveis

Primitivas técnicas, não conceitos de negócio. Cada uma precisa da
justificação acima antes de entrar:

- **Identificador (UUID)** — convenção de chave substituta, transversal por
  decisão de modelação (ADR-002). Pode ser convenção em vez de tipo
  partilhado.
- **Dinheiro/Moeda** — o Rivo é multi-moeda (AOA, USD, EUR). Só entra se
  `finance` **não** for o dono natural da semântica cambial. Por omissão
  não é: taxas, conversão e política cambial pertencem a `finance`; outros
  contextos referenciam montantes, não os interpretam.
- **Período/Intervalo de datas** — usado por Orçamento, Atribuição de Cargo,
  Contrato, Delegação. Primitiva estrutural sem regra de negócio.
- **Correlation ID** — exigido transversalmente por Auditoria e logging
  (ADR-002). Infra-estrutural.

## Como propor uma adição

1. Demonstrar que é primitiva estrutural, não conceito de negócio.
2. Demonstrar que ≥2 módulos já precisam dela — não "poderão precisar".
3. Demonstrar que colocá-la num módulo dono e expô-la por contrato não
   funciona (ex.: criaria ciclo de dependência).
4. Registar como ADR.

Na dúvida, deixar duplicado. Duplicação é mais barata de desfazer do que
acoplamento prematuro.
