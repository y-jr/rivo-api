# Nomenclatura

## Linguagem ubíqua

Os nomes em código seguem a linguagem do negócio para aquele módulo, não um
sinónimo técnico genérico.

Se `procurement` chama "Ordem de Compra", o código diz `OrdemCompra` (ou
`PurchaseOrder`, conforme a decisão de idioma abaixo) — não `Order`.

Quando dois módulos usam a mesma palavra para coisas diferentes (Factura em
`commercial`/AR vs. em `procurement`/AP), o namespace do módulo é o
desambiguador. Não inventar sufixos artificiais.

Termo ambíguo resolve-se contra
[domain/domain-map.md](../domain/domain-map.md) e o ficheiro do módulo dono
**antes** de nomear seja o que for.

## Termos com significado fixado

Estes têm significado decidido e não devem ser usados de outra forma:

| Termo | Significado | Dono |
|---|---|---|
| **Perfil de Acesso** | o que o utilizador pode ver/fazer no sistema | `identity` |
| **Cargo** | posição organizacional | `hr` |
| **Departamento** | unidade organizacional | `hr` |
| **Centro de Custo** | dimensão financeira; mapeamento a departamento é opcional | `finance` |
| **Orçamento** | tecto de controlo anual/mensal por centro de custo | `finance` |
| **Previsão de Custos Departamentais** | input mensal ao carregamento de caixa | `finance` |
| **Contrato de Trabalho** | relação laboral | `hr` |
| **Contrato Comercial** | contrato de venda | `commercial` |

Nunca usar "Perfil" e "Cargo" como sinónimos. Nunca usar "Departamento" e
"Centro de Custo" como sinónimos. Ver ADR-005 e ADR-006.

## Estrutura

- O nome do módulo é o namespace de topo de tudo o que esse módulo possui.
- As camadas nomeiam-se consistentemente em todos os módulos: `Api`,
  `Application`, `Domain`, `Infrastructure`.
- Schema da base de dados = nome do módulo, em minúsculas: `identity`, `hr`,
  `payroll`, `finance`, `procurement`, `commercial`, `approval`, `fiscal`,
  `projects`, `fleet`, `inventory`, `documents`, `notifications`, `audit`.

## Convenções C#

- Tipos, métodos e propriedades: `PascalCase`.
- Parâmetros e variáveis locais: `camelCase`.
- Interfaces com prefixo `I`.
- Um tipo público por ficheiro; nome do ficheiro = nome do tipo.

## Convenções da base de dados

- Tabelas e colunas: `snake_case`, singular para a entidade
  (`colaborador`, não `colaboradores`).
- Chave primária: `id` (`uniqueidentifier`, UUIDv7).
- Concorrência optimista: coluna `version`.
- FK: `<entidade>_id`. FK entre schemas qualifica o schema
  (`hr.colaborador(id)`).
- Tabelas de ligação a documentos: `<entidade>_documento`.
- Vigência temporal (dados fiscais, ADR-011): `vigente_desde`,
  `vigente_ate`.
- Valores monetários: `numeric` com precisão explícita.
- Identificadores sempre em minúsculas — evita ter de os citar com aspas.

## Evitar

- Sufixos genéricos que escondem intenção: `Manager`, `Helper`, `Util`,
  `Service` como catch-all. Nomear a responsabilidade.
- Abreviaturas que o negócio não usa.
