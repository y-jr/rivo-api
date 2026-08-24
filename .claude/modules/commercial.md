# commercial — Comercial / CRM

**Classificação:** core domain.

## Responsabilidade

Captação, negociação, contratação e cobrança de clientes. **Dono do
Cliente.**

Cadeia: lead → oportunidade → proposta → contrato comercial → factura
(handoff a `finance`/AR) → acção de cobrança.

## Conceitos

| Conceito | Notas |
|---|---|
| Cliente | nome, contactos, NIF — **dono confirmado** |
| Lead / Oportunidade (Deal) | estágio, valor; pipeline |
| Proposta | pode gerar Pedido de Aprovação (descontos, condições especiais) |
| Contrato Comercial | com SLA; pertence a `commercial` (ADR-009) |
| Acção de Cobrança | referencia factura de `finance` |

## Possui

Cliente, Lead/Oportunidade, Proposta, Contrato Comercial, Acção de
Cobrança, Tipos de serviço e templates de proposta.

## Depende de

`hr` (`ReferenciaColaborador` do dono comercial), `fiscal` (conformidade e
determinação de imposto), `approval` (descontos), `documents` (propostas,
contratos), `finance` (estado de facturação), `audit`, `notifications`.

## Consumido por

`finance` (cliente, base da factura de venda → AR), `projects`
(facturação de projecto), Portal do Cliente (read model).

## Contratos publicados

- Registo de Cliente (consumido por `finance` e pelo Portal do Cliente).
- Contrato Comercial e condições, para facturação.

## Eventos

- `PropostaAceite`
- `ContratoComercialAssinado`

## Não pode

- Emitir ou possuir a factura de venda — `commercial` fornece a base;
  `finance`/AR possui a factura e o recebimento.
- Calcular imposto — consulta `fiscal`.
- Possuir stock ou disponibilidade — consulta `inventory`.
- Fundir Contrato Comercial com Contrato de Trabalho (`hr`) — são conceitos
  distintos (ADR-009).

## Regras de negócio

- Descontos e condições especiais acima do permitido submetem a `approval`.
- A factura de venda tem de cumprir os requisitos de documento fiscal
  determinados por `fiscal` (numeração, campos obrigatórios).
- Sem eliminação física de documentos fiscais (BR-14).

### Isenção de IVA — invariante de emissão

Da DS.120 v1.4 (especificação de facturação electrónica):

```
taxCode ∈ { ISE, NS }  →  taxExemptionCode obrigatório
```

Uma linha de factura com `taxCode` igual a `ISE` (isento) ou `NS` (não
sujeito), **sem** `taxExemptionCode` válido e **activo à data do documento**,
é inválida e não pode ser emitida.

É invariante de domínio, não validação de interface. O código é obtido de
`fiscal` à data do facto gerador (ADR-011) — códigos revogados (ex.: M10,
M16, revogados pela Lei n.º 14/23) continuam válidos para documentos
históricos mas não para emissão nova.

**Caso sem código oficial:** a isenção do artigo 14.º/1 f) do CIVA (doações
ao Estado) não tem código atribuído na DS.120 v1.4. Bloquear a emissão que a
invoque — **não inventar código**.

## Perguntas em aberto

- Onde vive a política de preços e descontos: `commercial` ou configuração
  administrativa?
- Limiares de desconto que disparam aprovação.

## Estado

**Reduzido ao Cliente e iniciado em 2026-08-24 — ADR-036.**

**As cinco camadas existem desde 2026-08-24**, com schema `commercial`,
migração aplicada e rotas alcançáveis.

O objectivo passou a ser emitir. Emitir precisa de cliente; não precisa do
funil comercial. Lead, Oportunidade, Proposta, Contrato Comercial e Acção de
Cobrança continuam por fazer.

`Rivo.Commercial.Contracts` e `Rivo.Commercial.Domain`, com 20 testes de
domínio.

### O que já está imposto

| Regra | Forma concreta |
|---|---|
| Nome, NIF e morada de facturação obrigatórios | São o que o SAF-T exige do elemento `Customer`. O que não for capturado na transacção não se reconstrói depois |
| Morada é objecto de valor com detalhe, cidade e país | Substitui-se inteira. Uma morada parcial passaria na aplicação e falharia na exportação |
| País em ISO 3166-1 alpha-2 | `AO`. Duas letras, ou recusa |
| NIF normalizado sem espaços, em maiúsculas | Evita duplicados que só diferem no espaçamento |
| Desactivar, nunca eliminar | BR-14. Um cliente referenciado por facturas emitidas é parte desses documentos |
| `Version` em `Customer` | ADR-025 |

### Duas ausências deliberadas

**Sem validação de formato do NIF.** As regras de composição do NIF angolano
não estão verificadas em fonte primária neste repositório, e `CLAUDE.md` proíbe
implementar regras fiscais a partir de levantamento provisório. Um validador
inventado recusaria clientes legítimos — falha pior do que a ausência, porque
parece correcta.

**A unicidade do NIF não está no domínio.** É invariante sobre o conjunto de
clientes, e o agregado não vê o conjunto. Pertence a um índice único em
`commercial.customer` mais a verificação na camada Application. Ainda por
fazer — está registado aqui para não passar por esquecimento.

### Consequência: não há consumidor final

O NIF é obrigatório, portanto **não se factura a quem não o forneça**. Se o
negócio vender a particulares não identificados, falta o conceito — e a
convenção angolana para esse caso não está levantada. Registado em
[pending-decisions](../state/pending-decisions.md).

### Rotas

| Método | Rota | Permissão |
|---|---|---|
| GET | `/commercial/customers?includeInactive=` | `commercial.customers.read` |
| GET | `/commercial/customers/{customerId}` | `commercial.customers.read` |
| POST | `/commercial/customers` | `commercial.customers.write` |
| POST | `/commercial/customers/{customerId}/details` | `commercial.customers.write` |
| POST | `/commercial/customers/{customerId}/status` | `commercial.customers.write` |

**Não há `DELETE`**, e é BR-14 a aparecer na forma da API: desactiva-se pelo
endpoint de estado.

NIF repetido devolve **`409`** com o identificador do cliente que já existe —
quem tentou registar quase de certeza quer trabalhar com esse, e sem o
identificador teria de o procurar às cegas.

Morada indicada em parte devolve **`400`**: é objecto de valor, vai inteira ou
não vai.

`Sales` deixa de ser perfil vazio — recebe `commercial.customers.read` e
`.write`.
