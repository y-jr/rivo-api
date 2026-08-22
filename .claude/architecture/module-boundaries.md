# Fronteiras de Módulo

## O que é um módulo

Um bounded context: linguagem ubíqua própria, regras de negócio próprias,
camadas API/Application/Domain próprias, schema próprio na base de dados e
fronteira pública explícita.

## O que um módulo possui

- Os seus agregados, entidades, value objects e serviços de domínio.
- As invariantes que protegem esses conceitos.
- Os seus casos de uso e fronteiras de transacção.
- **O seu schema e as suas tabelas**, em exclusivo.

Partilhar a mesma instância de base de dados não é partilhar ownership.

## O que um módulo não pode fazer

- Ler ou escrever tabelas de outro módulo.
- Aceder a repositórios ou implementações de persistência de outro módulo.
- Referenciar tipos de `Domain` ou `Infrastructure` de outro módulo.
- Expor as suas entidades de domínio como contrato entre módulos.
- Assumir invariantes internas de outro módulo.
- Copiar para as suas tabelas atributos que outro módulo possui (ficam
  obsoletos em silêncio) — excepto o snapshot deliberado de `approval`.

## Contratos públicos

A fronteira pública de um módulo é composta apenas por:

- interfaces de serviço aplicacional
- contratos de pedido/resposta (DTOs)
- contratos de evento de integração

Representam capacidades ou factos — nunca detalhes de implementação.

**Alterar um contrato público é uma alteração de fronteira** e trata-se
como tal.

## Contratos publicados conhecidos

Os que estão já determinados em `docs/`:

| Módulo | Contrato | Consumidores |
|---|---|---|
| `hr` | `ReferenciaColaborador` — id, nome de exibição, estado, departamento, cargo actual, utilizador (opcional) | todos os contextos que referenciam pessoas |
| `approval` | Submissão de Pedido de Aprovação; consulta de estado; notificação de decisão | procurement, finance, payroll, hr, commercial |
| `finance` | Disponível orçamental (leitura para verificação em `approval`) | approval |
| `fiscal` | Determinação fiscal; requisitos de documento fiscal | commercial, procurement, finance |
| `documents` | Armazenar/obter documento e versões | hr, finance, commercial, payroll, fiscal, procurement |
| `audit` | Registar evento de auditoria | todos |
| `notifications` | Entregar notificação | todos |

## Como os módulos comunicam

### 1. Contrato síncrono

Quando o chamador precisa de resposta imediata.

```
approval  ──"quem ocupa o cargo X hoje?"──>  hr
approval  ──"há orçamento disponível?"────>  finance
```

O chamador conhece apenas o contrato publicado.

### 2. Evento de integração

Quando um módulo publica um facto a que outros podem reagir.

```
approval  ──DecisaoConcluida──>  procurement  (gera Ordem de Compra)
finance   ──PagamentoExecutado──>  audit, notifications
```

Preferir evento quando: não é necessária resposta imediata; a interacção é
um facto ocorrido; uma dependência síncrona criaria acoplamento
desnecessário.

**Não usar eventos por omissão.** Cada evento tem de resolver um problema
identificável.

## O anti-padrão a evitar: God Module

O `approval` é o candidato natural a God Module — precisa de saber cargos
(`hr`) e orçamento (`finance`) para decidir.

Impedimento:

- `approval` **nunca** lê tabelas de `hr` ou `finance`.
- Consome dois contratos estreitos, explícitos e versionados.
- `approval` não conhece o significado de negócio do que aprova. Recebe
  tipo de processo, valor, departamento e requisitante; devolve uma
  decisão. O módulo de origem interpreta e aplica a consequência.

O mesmo teste aplica-se a qualquer capacidade transversal: se precisa de
conhecer os detalhes internos dos seus consumidores, a fronteira está mal
desenhada.

## Alterar uma fronteira

Antes de introduzir uma dependência nova entre módulos:

1. Identificar o módulo dono em
   [domain/domain-map.md](../domain/domain-map.md).
2. Confirmar que o conceito não tem já outro dono.
3. Identificar exactamente que informação ou capacidade é necessária.
4. Decidir entre contrato síncrono e evento.
5. Confirmar contra a tabela de dependências permitidas em
   [dependency-rules.md](dependency-rules.md).
6. Confirmar que não cria ciclo.
7. Registar como ADR se não for trivial.
