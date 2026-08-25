# Testes

Cada funcionalidade traz os seus testes como parte da alteração, não como
trabalho posterior.

## Por camada

### Domain — a prioridade

Invariantes, regras de negócio e comportamento de agregado, testados em
isolamento: sem infraestrutura, sem framework, sem base de dados.

**Se uma regra de domínio precisa de base de dados ou de contexto HTTP para
ser testada, ela vazou da camada de domínio.** Isso é um defeito de
arquitectura, não um problema de teste.

Aplica-se com particular força às invariantes de `approval` (segregação de
funções, alçadas, anti-fraccionamento) — ADR-008 exige que vivam e sejam
testadas no domínio, não em RLS.

### Application

Orquestração de casos de uso, contra fakes/implementações em memória dos
ports (repositórios, gateways), não contra infraestrutura real — excepto
quando o teste é especificamente de integração.

### Infrastructure

Testes de integração contra a tecnologia real (SQL Server efectivo) para as
peças que implementam ports: repositórios, gateways externos. É aqui que
tocar numa dependência real é correcto, não um atalho.

Verificar que as políticas RLS fazem o que dizem é teste de integração
válido — mas **em acréscimo** ao teste da invariante no domínio, nunca em
substituição (ADR-008).

### API

Fino. Mapeamento de pedido/resposta e códigos de estado. Não lógica de
negócio — essa já está coberta.

## Entre módulos

Testar o contrato publicado de um módulo a partir do lado consumidor, com
fakes. **Nunca** alcançando as internals do módulo fornecedor.

Exemplo: um teste de `approval` usa um fake de `ReferenciaColaborador`;
não toca em tabelas de `hr`.

## Cenários que exigem cobertura explícita

Decorrem dos requisitos absorvidos do SGAP e não devem faltar:

- **IRT:** o INSS do trabalhador (3%) é deduzido antes da matéria
  colectável; o patronal (8%) **nunca** é. Erro aqui afecta todos os
  recibos.
- **IRT:** um cálculo sobre facto gerador de exercício anterior aplica a
  tabela vigente **à data do facto**, não a actual (ADR-011).
- **IRT:** as descontinuidades da parcela fixa nas fronteiras de escalão são
  comportamento esperado — fixar em teste para ninguém as "corrigir".
- **IVA:** uma linha com `taxCode` em {`ISE`, `NS`} sem `taxExemptionCode`
  válido e activo à data do documento não pode ser emitida.
- **IVA:** um código revogado (M10, M16) é aceite na leitura de documentos
  históricos e recusado na emissão nova.
- Quem submete não consegue decidir sobre o próprio pedido (BR-2).
- Pagamento não executável sem decisão "Aprovado" registada (BR-1).
- Revalidação de estado e de saldo no momento da execução (BR-5).
- Aprovadores congelados na submissão não são recalculados por alteração
  organizacional posterior (BR-6).
- Decisões concorrentes: duas pessoas a decidir em simultâneo (BR-17).
- Tentativa não autorizada fica registada em `audit`, não só bloqueada
  (BR-12).

## Princípios

- Preferir colaboradores reais dentro de uma camada; usar mock apenas na
  fronteira de port entre camadas.
- Uma suite que passa com a regra de negócio apagada não está a testar a
  regra. Asserir comportamento, não detalhes de implementação.

## Stack

**xUnit v2.9.3, sem biblioteca de asserções** (ADR-022).

Um projecto de teste por domínio de módulo, em
`tests/Modules/<Módulo>/Rivo.<Módulo>.Domain.Tests/`, a espelhar `src/`. Um
projecto de teste referencia **um** domínio e mais nada — um projecto único
que referenciasse os catorze seria o sítio onde todos os módulos se
encontram.

Versões e propriedades comuns em `tests/Directory.Build.props`. Nomes de teste
em `MethodUnderTest_Scenario_ExpectedOutcome`, em inglês; o comentário que
explica porque é que a regra existe, em português.

```
dotnet test
```

## Estado da cobertura

_Contagens de 2026-08-25. **326 testes** em 13 projectos._

| Camada | Estado |
|---|---|
| Domain | **284 testes**, 9 módulos — `hr` 129, `finance` 45, `commercial` 20, `notifications` 20, `fiscal` 18, `approval` 17, `documents` 16, `audit` 10, `identity` 9 |
| Application | **8 testes** em `identity`. Os restantes oito módulos por cobrir |
| Infrastructure | **4 testes** em `notifications`, SQL Server real (ADR-026, ADR-029). Restantes oito módulos por cobrir |
| API do host | **9 testes** em `tests/Rivo.Api.Tests` — tradução de excepções em códigos HTTP (ADR-035). Nasceu porque isto não é testável em nenhuma das outras camadas: as de domínio não conhecem HTTP, as de arquitectura verificam forma e não comportamento |
| API de módulo | Nenhum (as suites PowerShell tocam-lhe indirectamente) |
| Arquitectura | **21 testes** (ADR-024, ADR-025) |

As nove suites PowerShell (**116 casos**) continuam a valer como smoke
end-to-end. Não substituem teste de domínio, nem o inverso.

**O desequilíbrio é o de sempre, e cresceu:** o domínio tem 284 testes e a
Application tem 8. Cada módulo novo acrescenta domínio coberto e Application
descoberta — o que lá existe é exercitado indirectamente pelas suites
caixa-preta, que testam o sistema montado e não as unidades.

## Integração

**Testcontainers com SQL Server real** (ADR-026, ADR-029), em
`tests/Modules/<Módulo>/Rivo.<Módulo>.Infrastructure.Tests/`. A imagem
acompanha a do `docker-compose.dev.yml`: testar contra uma versão diferente
daquela em que se corre é uma classe de defeitos que só aparece em produção.

O fixture partilhado vive em `tests/Rivo.TestSupport`, que **não referencia
módulo nenhum e não pode passar a referenciar**. A `[CollectionDefinition]`
tem de estar no assembly de cada teste — constrangimento do xUnit.

**Exige Docker.** Os testes de domínio e de arquitectura continuam a correr
sem ele.

## Em aberto

- Testes de Application e de API.
- Testes de integração nos restantes quatro módulos.

Ver [architecture/technology-decisions.md](../architecture/technology-decisions.md).
