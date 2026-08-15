# Rivo — Regras Fiscais Angolanas: Levantamento

> ## ⚠ VERIFICAÇÃO PROFISSIONAL AINDA OBRIGATÓRIA
>
> Este documento consolida informação de fontes secundárias e de
> contribuições do cliente. **Não substitui verificação por contabilista ou
> fiscalista qualificado contra o Diário da República.**
>
> Cada item tem rótulo de confiança em §6. Os itens marcados
> **⚠ CONFLITO** não podem ser implementados de todo.
>
> Errar um escalão de IRT significa reter imposto a mais ou a menos sobre
> salários reais. O risco é legal e financeiro, não técnico.

**Última actualização:** 2026-08-10

---

## 0. Fontes

### Fontes primárias — acesso falhado

| Fonte | Resultado |
|---|---|
| AGT — Portal do IVA (`agt.minfin.gov.ao/PortalAGT/#!/iva/…`) | SPA; conteúdo não servido no HTML |
| Primavera BSS — IRT (`ao.primaverabss.com/pt/blog/irt-alteracoes/`) | HTTP 403 |
| INSS — Legislação (`www.inss.gov.ao/legislacao`) | Erro de TLS: certificado não verificável |

Via alternativa possível: `portal.inss.gov.ao` (subdomínio diferente) aparenta
servir PDFs de legislação sem o problema de certificado.

### Fontes secundárias e contribuições

- KPMG Angola — Lei do OGE 2026
- Angolex — tabela IRT 2026
- PwC Angola, Wisedat — regimes de IVA
- **Contribuição do cliente (2026-08-10)** — tabela IRT (origem: imagem),
  dedutibilidade do INSS, tabela de códigos de isenção de IVA, confirmação
  de taxas INSS

### Fonte normativa nova, ainda por obter

**DS.120 v1.4 — Especificação Técnica de Facturação Electrónica**
(Agosto de 2025). Referida pelo cliente. Define as regras de preenchimento de
`taxCode` e `taxExemptionCode` na facturação electrónica, e é complementar ao
XSD do SAF-T (`docs/rivo-fiscal-saft-ao-v1.md`).

**Deve ser obtida em versão oficial da AGT.** As referências disponíveis
apontam para cópias em Scribd, que não são fonte fiável para especificação
normativa.

---

## 1. IRT — Imposto sobre o Rendimento do Trabalho

### 1.1 Fonte adoptada: KPMG Angola

**Decisão do cliente (2026-08-10): a KPMG Angola é a fonte a adoptar.**

O que a KPMG **estabelece** (verificado directamente na publicação sobre a
Lei do OGE 2026):

| Facto | Estado |
|---|---|
| Instrumento: **Lei n.º 14/25, de 30 de Dezembro de 2025**, em vigor 01/01/2026 | Confirmado |
| **Limite de isenção de IRT: 150.000 Kz** — "anteriormente 100.000 Kz" | Confirmado |
| Grupo C: 6,5% sobre volume ≥ 10.000.000 Kz | Confirmado |
| Grupo C agrícola/florestal/pecuário/pesca acima de 10.000.000 Kz: 10% | Confirmado |
| Suspensa em 2026 a regra dos 4× a tabela de lucros mínimos | Confirmado |

O que a KPMG **não** estabelece:

> **A publicação da KPMG não contém a tabela de escalões.** Verificado
> explicitamente: sem limites de rendimento, sem parcelas fixas, sem taxas,
> e sem referência ao Anexo I.

### 1.2 O que fica resolvido e o que não fica

**Resolvido pela KPMG:**

A tabela de isenção **70.000 Kz é histórica**, não vigente. A KPMG indica
que o limite anterior ao actual era 100.000 Kz — ou seja, os 70.000 são
pelo menos duas gerações atrás. A Tabela A (§1.3) passa a ter tratamento de
dado histórico, com `vigente_ate` preenchido (ADR-011).

**Corroborado por duas fontes independentes:**

A estrutura de escalões **acima de 150.001 Kz** é idêntica na contribuição do
cliente e na Angolex — mesmos limites, mesmas parcelas fixas, mesmas taxas.
Duas transcrições independentes coincidentes elevam a confiança nesta parte.

**⚠ Continua por confirmar — e é o ponto crítico:**

Se a isenção é 150.000 e o escalão seguinte (150.001–200.000) mantém a
parcela fixa de **12.500**, então um rendimento colectável de 150.001 Kz
paga 12.500 Kz de imposto — um **salto de 12.500 Kz por 1 Kz de rendimento
adicional**.

Duas leituras possíveis, ambas plausíveis:

1. **O salto é real.** A tabela histórica tem o mesmo padrão (isento até
   70.000; 70.001 paga parcela fixa de 3.000), o que sugere que estes saltos
   são característica do desenho da tabela de IRT angolana.
2. **A parcela fixa do primeiro escalão tributado foi ajustada** na Lei
   n.º 14/25 e a transcrição da Angolex arrastou o valor antigo.

**Isto é questão de direito fiscal, não de engenharia.** Só o Anexo I da
Lei n.º 14/25, ou um fiscalista, resolve.

**Posição a adoptar até lá:** isenção de 150.000 Kz como facto assente;
escalões acima de 150.001 como dados provisórios carregáveis para
desenvolvimento e teste, **bloqueados para produção** até validação da
parcela fixa do escalão 150.001–200.000.

### 1.3 Tabela A — HISTÓRICA, não vigente (isenção 70.000)

Conservada por exigência de ADR-011: é necessária para recalcular ou
reemitir períodos anteriores. **Não usar para cálculo corrente.**

| # | Rendimento (Kz) | Parcela Fixa | Taxa | Excesso de |
|---|---|---|---|---|
| 1 | Até 70.000 | — | isento | — |
| 2 | 70.001 – 100.000 | 3.000 | 10,0% | 70.000 |
| 3 | 100.001 – 150.000 | 6.000 | 13,0% | 100.000 |
| 4 | 150.001 – 200.000 | 12.500 | 16,0% | 150.000 |
| 5 | 200.001 – 300.000 | 31.250 | 18,0% | 200.000 |
| 6 | 300.001 – 500.000 | 49.250 | 19,0% | 300.000 |
| 7 | 500.001 – 1.000.000 | 87.250 | 20,0% | 500.000 |
| 8 | 1.000.001 – 1.500.000 | 187.250 | 21,0% | 1.000.000 |
| 9 | 1.500.001 – 2.000.000 | 292.000 ⚠ | 22,0% | 1.500.000 |
| 10 | 2.000.001 – 2.500.000 | 402.250 | 23,0% | 2.000.000 |
| 11 | 2.500.001 – 5.000.000 | 517.250 | 24,0% | 2.500.000 |
| 12 | 5.000.001 – 10.000.000 | 1.117.250 | 24,5% | 5.000.000 |
| 13 | Acima de 10.000.001 | 2.342.250 | 25,0% | 10.000.000 |

⚠ **Divergência no 9.º escalão:** esta fonte indica `292.000`; a Angolex
indica `292.250`. Uma das transcrições está errada — provavelmente OCR.
Confirmar contra o Anexo I.

### 1.4 Tabela B — vigente desde 01/01/2026 (Lei n.º 14/25)

Isenção **150.000 Kz** (KPMG, confirmado). Escalões idênticos à Tabela A a
partir de 150.001, sem os três escalões inferiores:

| # | Rendimento (Kz) | Parcela Fixa | Taxa | Excesso de |
|---|---|---|---|---|
| 1 | Até 150.000 | — | isento | — |
| 2 | 150.001 – 200.000 | 12.500 ⚠ | 16,0% | 150.000 |
| 3 | 200.001 – 300.000 | 31.250 | 18,0% | 200.000 |
| 4 | 300.001 – 500.000 | 49.250 | 19,0% | 300.000 |
| 5 | 500.001 – 1.000.000 | 87.250 | 20,0% | 500.000 |
| 6 | 1.000.001 – 1.500.000 | 187.250 | 21,0% | 1.000.000 |
| 7 | 1.500.001 – 2.000.000 | 292.250 ⚠ | 22,0% | 1.500.000 |
| 8 | 2.000.001 – 2.500.000 | 402.250 | 23,0% | 2.000.000 |
| 9 | 2.500.001 – 5.000.000 | 517.250 | 24,0% | 2.500.000 |
| 10 | 5.000.001 – 10.000.000 | 1.117.250 | 24,5% | 5.000.000 |
| 11 | Acima de 10.000.001 | 2.342.250 | 25,0% | 10.000.000 |

⚠ **Escalão 2** — parcela fixa por confirmar; ver §1.2. Produz salto de
12.500 Kz na fronteira da isenção.

⚠ **Escalão 7** — divergência de transcrição: 292.250 (Angolex) vs. 292.000
(contribuição do cliente). Diferença de 250 Kz.

### 1.5 Descontinuidades da parcela fixa

Ambas as tabelas produzem **saltos** na fronteira de escalão — na Tabela A,
70.001 paga parcela fixa de 3.000; na Tabela B, 150.001 paga 12.500.

Se se confirmarem, o motor de cálculo tem de os reproduzir tal como estão,
sem "suavizar". Fixar em teste como comportamento esperado, para que ninguém
os "corrija" mais tarde.

Enquanto a parcela fixa do escalão 2 da Tabela B não estiver validada, o
salto é **anomalia por esclarecer**, não comportamento assente.

### 1.5 Fórmula de cálculo

```
IRT = Parcela Fixa + (Matéria Colectável − Excesso de) × Taxa
```

Aplica-se a partir do 2.º escalão.

### 1.6 Matéria colectável — INSS é dedutível

**Confirmado.** Artigo 7.º do Código do IRT (Lei n.º 18/14, de 22 de
Outubro): deduzem-se primeiro as contribuições obrigatórias para a Segurança
Social e depois as componentes remuneratórias não sujeitas ou isentas.

```
Salário Bruto
   − INSS do trabalhador (3%)
   − Componentes não sujeitas / isentas
   = Matéria Colectável do IRT
   → identificar escalão
   → Parcela Fixa + Taxa × Excesso
   = IRT
```

**Distinção crítica para `payroll`:** deduz-se **apenas a parcela do
trabalhador (3%)**. A contribuição patronal (8%) é custo da empresa e
**nunca** é subtraída ao rendimento do trabalhador para efeitos de IRT.

Exemplo (Tabela B, bruto 250.000):

```
250.000 − 7.500 (3% INSS)   = 242.500  matéria colectável
242.500 → escalão 200.001–300.000
IRT = 31.250 + (242.500 − 200.000) × 18%
    = 31.250 + 7.650
    = 38.900 Kz
```

### 1.7 Grupos

- **Grupo A** — trabalho por conta de outrem. Retenção na fonte pela
  entidade empregadora.
- **Grupo B** — trabalho independente. *Regras não recolhidas.*
- **Grupo C** — actividades industriais e comerciais (via KPMG):
  - Volume ≥ 10.000.000 Kz: matéria colectável = volume de vendas de bens e
    serviços não sujeitos a retenção na fonte, taxa **6,5%**
  - Com contabilidade organizada: regras gerais do Imposto Industrial
  - Actividades agrícolas/florestais acima de 10.000.000 Kz: **10%**
  - Suspensa em 2026 a regra de tributação sobre vendas totais ao atingir 4×
    o limiar da tabela de lucros mínimos

### 1.8 Isenções adicionais

Pessoas com deficiência de grau **≥ 50%**, mediante documentação oficial.
*(Fonte secundária; confirmar.)*

### 1.9 Por recolher

- Tratamento de subsídios: alimentação, transporte, férias, Natal.
- Regras completas do Grupo B.

---

## 2. IVA — Imposto sobre o Valor Acrescentado

### 2.1 Taxas

| Taxa | Aplicação |
|---|---|
| **14%** | Normal |
| **5%** | Importação ou transmissão de equipamentos industriais pelo fabricante — mediante requerimento e aprovação da administração tributária (Lei n.º 14/25) |

Outras taxas reduzidas referidas em fontes diversas (5%, 7%, 2% para
Cabinda) **não foram confirmadas**. Não assumir.

### 2.2 Regimes por volume de negócios

| Regime | Limiar | Comportamento |
|---|---|---|
| **Exclusão** | < 25.000.000 Kz | Sem liquidação nas vendas; facturas sem imposto; **sem direito a dedução** — o IVA suportado é custo |
| **Simplificado** | ≤ 350.000.000 Kz (12 meses anteriores) | Sem liquidação; pagamento **trimestral** de metade da taxa (14% × 50% = 7%) sobre vendas recebidas no trimestre |
| **Geral** | Acima dos limiares | Liquidação e dedução normais |

**Mudança de regime (Lei n.º 14/25):** ultrapassado o limiar, o sujeito
passivo é obrigado a alterar de regime **até ao final do mês seguinte** ao
da operação que originou a alteração. A administração tributária pode
alterar oficiosamente.

**Implicação para o Rivo:** o regime de IVA é um **estado da empresa com
vigência temporal**, dependente de volume de negócios acumulado e com prazo
legal de transição. Não é configuração estática. Encaixa em ADR-011.

### 2.3 Códigos de isenção

**Instrumento:** Código do IVA republicado pela **Lei n.º 14/23**.
**Especificação de preenchimento:** DS.120 v1.4 (por obter em versão
oficial).

#### Operações internas — artigo 12.º

| Código | Artigo | Estado | Motivo |
|---|---|---|---|
| M10 | 12.º/1 a) | 🔴 **REVOGADO** pela Lei n.º 14/23 | Transmissão de bens alimentares (Anexo I) |
| M11 | 12.º/1 b) | Activo | Medicamentos para fins terapêuticos e profilácticos |
| M12 | 12.º/1 c) | Activo | Cadeiras de rodas e veículos semelhantes para pessoas com deficiência; aparelhos para invisuais ou correcção de visão/audição |
| M13 | 12.º/1 d) | Activo | Livros, incluindo formato digital, excepto conteúdo pornográfico |
| M14 | 12.º/1 e) | Activo | **Locação** de bens imóveis, excepto alojamento em actividade hoteleira ou similar |
| M15 | 12.º/1 f) | Activo | **Transmissão** de bens imóveis |
| M16 | 12.º/1 g) | 🔴 **REVOGADO** pela Lei n.º 14/23 | Jogos de fortuna ou azar e diversão social |
| M17 | 12.º/1 h) | Activo | Transporte colectivo de passageiros |
| M18 | 12.º/1 i) | Activo | Intermediação financeira, incluindo locação financeira, excepto quando haja taxa ou contraprestação específica e predeterminada |
| M19 | 12.º/1 j) | Activo | Seguro de saúde; seguros e resseguros do ramo vida |
| M20 | 12.º/1 k) | Activo | Produtos petrolíferos (Anexo V) |
| M21 | 12.º/1 l) | Activo | Ensino por estabelecimentos enquadrados na Lei de Bases do Sistema de Educação e Ensino |
| M22 | 12.º/1 m) | Activo | Serviço médico-sanitário por hospitais, clínicas, dispensários e similares, excepto estética |
| M23 | 12.º/1 n) | Activo | Transporte de doentes ou feridos em ambulâncias ou veículos apropriados, por organismos autorizados |
| M24 | 12.º/1 o) | Activo | Equipamentos e materiais médicos (Anexo IV do CIVA) |

> **⚠ Não usar descrições antigas.** Na versão republicada, **M14 = locação**
> de imóveis e **M15 = transmissão** de imóveis. As descrições anteriores
> ("bens imóveis destinados a fins habitacionais", "operações sujeitas a
> SISA") pertencem à versão revogada do CIVA.

#### Exportações e operações assimiladas — artigo 15.º

| Código | Alínea | Motivo |
|---|---|---|
| M30 | a) | Bens expedidos ou transportados para o estrangeiro pelo exportador |
| M31 | b) | Bens de abastecimento a bordo de embarcações em navegação marítima em alto mar |
| M32 | c) | Bens de abastecimento a bordo de aeronaves de companhias dedicadas principalmente ao tráfego internacional |
| M33 | d) | Bens de abastecimento de embarcações de salvamento, assistência marítima, pesca costeira e de guerra que deixem Angola |
| M34 | e) | Transmissões, transformações, reparações, manutenção, frete e aluguer de embarcações e aeronaves afectas ao tráfego internacional, e serviços conexos |
| M35 | f) | Relações diplomáticas e consulares, por acordo ou convenção internacional |
| M36 | g) | Organismos internacionais reconhecidos por Angola, ou seus membros |
| M37 | h) | Tratados e acordos internacionais |
| M38 | i) | Transporte de pessoas de ou para o estrangeiro |

#### Importações — artigo 14.º

| Código | Artigo | Motivo |
|---|---|---|
| M80 | 14.º/1 a) | Importações definitivas de bens cuja transmissão interna seja isenta |
| M81 | 14.º/1 b) | Ouro, moedas ou notas de banco, pelo Banco Nacional de Angola |
| M82 | 14.º/1 c) | Bens para ofertas com fins filantrópicos ou para atenuar calamidades naturais, reconhecidos pela Administração Tributária |
| M83 | 14.º/1 d) | Mercadorias e equipamentos exclusiva e directamente afectos a operações petrolíferas e mineiras |
| M84 | 14.º/1 e) | Moeda estrangeira, por instituições financeiras bancárias |
| M85 | 14.º/2 a) | Tratados e acordos internacionais de que Angola seja parte |
| M86 | 14.º/2 b) | Relações diplomáticas e consulares, quando resulte de tratado ou acordo |

> **⚠ Lacuna de código.** A Lei n.º 14/23 acrescentou uma alínea **f)** ao
> artigo 14.º/1 — importações de bens destinados a doação ao Estado, seus
> organismos e Autarquias Locais. **Não existe código oficial conhecido**
> para esta situação na DS.120 v1.4.
>
> **Não inventar `M87`.** Registar a isenção legal na tabela com o código
> **pendente de atribuição pela AGT**, e bloquear a emissão de factura que a
> invoque até haver código oficial.

#### Regime especial aduaneiro — artigo 16.º

| Código | Alínea | Motivo |
|---|---|---|
| M90 | a) | Importações sob controlo aduaneiro em zona franca, armazéns de regimes aduaneiros ou lojas francas |
| M91 | b) | Bens expedidos para essas zonas/depósitos e serviços conexos |
| M92 | c) | Transmissões efectuadas nesses regimes e serviços conexos |
| M93 | d) | Trânsito, draubaque ou importação temporária, e serviços conexos |
| M94 | e) | Reimportação pelo exportador, no mesmo estado, com isenção de direitos aduaneiros |

#### Códigos históricos — não são isenções

| Código | Significado | Tratamento |
|---|---|---|
| M00 | Regime Simplificado / antigo regime transitório (até 31/12/2020) | Histórico — só para leitura de documentos antigos |
| M02 | Operação **não sujeita** | **Não é isenção.** `taxCode = NS`, não `ISE` |
| M04 | Regime de Exclusão / antigo regime de não sujeição | Histórico |

**Não modelar M02 como isenção.** "Não sujeito" e "isento" são estados
fiscais distintos com tratamento distinto.

### 2.4 Regra de emissão de factura

Da DS.120 v1.4:

```
taxType = IVA  ∧  taxCode = ISE   →  taxExemptionCode obrigatório
taxType = IVA  ∧  taxCode = NS    →  taxExemptionCode obrigatório
```

O valor tem de constar das tabelas oficiais. Exemplo de linha:

```json
{
  "taxType": "IVA",
  "taxCode": "ISE",
  "taxPercentage": 0,
  "taxExemptionCode": "M11",
  "taxExemptionReason": "Isento nos termos da alínea b) do n.º 1 do artigo 12.º do CIVA"
}
```

**Invariante para `commercial`:** uma linha com `taxCode` em {`ISE`, `NS`} e
sem `taxExemptionCode` válido e activo à data do documento é inválida e não
pode ser emitida.

### 2.5 Modelo de dados dos códigos de isenção

Nos termos de ADR-011, a tabela não é `código → descrição`. É:

| Campo | Notas |
|---|---|
| `codigo` | M11, M15, M30… |
| `tipo_imposto` | IVA |
| `descricao` | Descrição do motivo |
| `motivo_factura` | Texto a imprimir: "Isento nos termos da alínea b) do n.º 1 do artigo 12.º do CIVA" |
| `artigo_legal` | 12 |
| `alinea_legal` | 1.b |
| `tipo_isencao` | INTERNA / EXPORTACAO / IMPORTACAO / ADUANEIRO |
| `estado` | ACTIVO / REVOGADO / PENDENTE_CODIGO |
| `vigente_desde` / `vigente_ate` | Vigência temporal (ADR-011) |
| `fonte` | "CIVA / Lei n.º 14/23" |

**Os códigos revogados (M10, M16) não são eliminados.** Passam a
`REVOGADO` com `vigente_ate` preenchido — continuam a ser necessários para
interpretar documentos históricos. Aplicação directa de ADR-011 e de BR-14.

---

## 3. INSS — Segurança Social

### 3.1 Taxas

| Contribuinte | Taxa |
|---|---|
| Entidade empregadora | **8%** |
| Trabalhador | **3%** |
| **Total** | **11%** |

Confirmado pelo cliente. Instrumento: Decreto Presidencial n.º 227/18
*(a confirmar)*; referências também a Decreto n.º 42/08.

As propostas noticiadas de aumento para 10%/5% **não estão em vigor**.

### 3.2 Base de incidência

Remuneração mensal ilíquida — remuneração base, subsídios e outras
prestações regulares decorrentes da relação laboral.

### 3.3 Relação com o IRT

Apenas os **3% do trabalhador** são dedutíveis à matéria colectável do IRT.
Os 8% patronais são custo da empresa. Ver §1.6.

### 3.4 Por recolher

- Existe tecto contributivo?
- Prazo de entrega das contribuições.
- Trabalhadores estrangeiros e expatriados.

---

## 4. Consequências arquitectónicas

1. **Taxas, escalões e códigos são dados com vigência**, nunca código —
   **ADR-011**. Este levantamento confirma-o de forma prática: existem duas
   tabelas de IRT em circulação e códigos de IVA revogados que têm de
   continuar legíveis.

2. **A ordem de cálculo do IRT é invariante de `payroll`:** INSS do
   trabalhador deduz-se **antes**; INSS patronal **nunca** se deduz. Erro
   aqui afecta todos os recibos.

3. **`commercial` não pode emitir factura isenta sem código válido à data.**
   Invariante de domínio, não validação de UI.

4. **Descontinuidades da parcela fixa são comportamento esperado**, não bug.
   Fixar em teste.

5. **Regime de IVA é estado temporal da empresa**, com transição obrigatória
   até ao fim do mês seguinte. Não é configuração.

---

## 5. Por obter

| # | Item | Bloqueia |
|---|---|---|
| 1 | **Anexo I da Lei n.º 14/25** — tabela de IRT oficial, para resolver o conflito §1.1 | `payroll` |
| 2 | **DS.120 v1.4 oficial da AGT** — especificação de facturação electrónica | `commercial`, `fiscal` |
| 3 | Código oficial para a isenção do artigo 14.º/1 f) | `commercial` |
| 4 | Confirmação do decreto vigente do INSS e existência de tecto | `payroll` |
| 5 | Tratamento de subsídios em IRT | `payroll` |
| 6 | Regras do Grupo B do IRT | `fiscal` |
| 7 | Prazos e formatos das declarações à AGT | `fiscal` |

---

## 6. Estado de verificação

| Item | Confiança |
|---|---|
| INSS dedutível ao IRT; só a parcela do trabalhador (art. 7.º CIRT, Lei 18/14) | **Alta** |
| INSS 8% empregador / 3% trabalhador | **Alta** |
| Fórmula IRT = Parcela Fixa + (MC − Excesso) × Taxa | **Alta** |
| IVA taxa normal 14% | **Alta** |
| Lei n.º 14/25, em vigor 01/01/2026 | **Alta** |
| Códigos de isenção M11–M24, M30–M38, M80–M86, M90–M94 | **Média-alta** |
| M10 e M16 revogados pela Lei n.º 14/23 | **Média-alta** |
| M14 = locação, M15 = transmissão de imóveis | **Média-alta** |
| Limiares de regime de IVA (25M / 350M) | **Média** |
| IVA reduzido 5% equipamentos industriais | **Média** |
| Grupo C: 6,5% e 10% agrícola | **Média** |
| **Isenção de IRT = 150.000 Kz desde 01/01/2026** | **Alta** — KPMG, fonte adoptada |
| Tabela de isenção 70.000 é histórica, não vigente | **Alta** — KPMG indica 100.000 como limite anterior |
| Estrutura de escalões acima de 150.001 | **Média-alta** — duas transcrições independentes coincidentes |
| Parcela fixa do escalão 150.001–200.000 (12.500) | **⚠ Por validar** — produz salto de 12.500 Kz na fronteira da isenção. Bloqueia produção |
| Parcela fixa do escalão 1.500.001–2.000.000 | **Média** — 292.250 vs. 292.000 |
| Código para art. 14.º/1 f) | **Inexistente — bloquear emissão** |
