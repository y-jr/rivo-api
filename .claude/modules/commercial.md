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

Não iniciado.
