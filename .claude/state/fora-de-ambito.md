# Fora de âmbito

O que **não** está a ser construído, e o que seria preciso saber para o
construir. Não é lista de recusas: é lista de coisas sem requisitos, para que a
ausência delas seja uma escolha visível e não um esquecimento.

Decidido a 2026-09-07, pelo utilizador, ao rever o painel de estado.

## Porque este ficheiro existe

O painel do frontend mostrava três módulos como **«por desenhar»**: ESG,
Helpdesk & Tickets, e Jurídico & Compliance. Tinham nome e uma frase, e mais
nada — sem ficha em `modules/`, sem ADR, sem lugar no roteiro de execução.

«Por desenhar» num painel de estado lê-se como *vem a caminho*. Não vinha.
Quatro entradas de menu apontavam para eles, e a sidebar prometia
funcionalidade que ninguém tinha decidido construir.

Saíram do registo e do menu. Ficam aqui.

⚠ **Isto não diz que nunca serão feitos.** Diz que hoje não são âmbito, e que
passam a sê-lo no dia em que houver requisitos — o que, pelo `CLAUDE.md`, não
posso inventar: *«Não inventar requisitos — se faltar informação, dizer de que
informação se depende.»*

## ESG — indicadores ambientais, sociais e de governança

**De que informação se depende:**

- Que indicadores, e por que norma? GRI, SASB, ou um formato exigido por
  financiador ou cliente? A escolha decide o modelo de dados inteiro.
- Quem os produz? Uma boa parte de ESG não se calcula do ERP — vem de facturas
  de energia, de contratos de trabalho, de auditorias externas.
- Para quem é o relatório? Um relatório interno e um relatório para financiador
  têm exigências de rastreabilidade diferentes.

**O que já existiria para aproveitar:** consumo de combustível está em `fleet`
(`FleetExpenseCategory.Fuel`), distância percorrida em `VehicleTrip`, e a
composição do quadro de pessoal em `hr`. Nada disso é um indicador ESG por si
só, mas é matéria-prima.

## Helpdesk & Tickets internos

⚠ **Não confundir com os tickets que existem.** `messaging` tem tickets desde o
ADR-046, e são outra coisa: **o cliente abre, a equipa comercial responde**. Um
helpdesk interno é o inverso — o colaborador abre, e alguém de dentro resolve.

**De que informação se depende:**

- Quem resolve? Um departamento fixo (TI), o gestor do departamento de quem
  abriu, ou uma fila com atribuição manual?
- Há categorias? O `messaging` recusou taxonomia de propósito — o assunto
  escrito pelo cliente é a categorização. Um helpdesk interno pode precisar do
  contrário: «avaria de equipamento» encaminha para sítio diferente de «acesso
  a sistema».
- Há SLA? O ADR-046 adiou SLA explicitamente. Um helpdesk sem prazo é uma caixa
  de correio.
- O que acontece a um ticket resolvido? Fecha, ou vai a validação de quem
  abriu?

**É o mais tratável dos três**, porque o padrão existe: `Conversation`/`Message`
com `Kind`, a máquina de estados de abrir/responder/fechar, o índice único
filtrado. Um helpdesk interno é a mesma máquina com outro protagonista, e a
pergunta aberta é quanto dele reutilizar e quanto separar.

## Jurídico & Compliance

**De que informação se depende:**

- «Contratos legais» são os contratos de trabalho que `hr` já tem, contratos
  com clientes e fornecedores, ou contratos societários? São três coisas com
  donos diferentes.
- «Auditoria interna» é a trilha de `audit`, que já existe e é append-only, ou
  é um processo de auditoria com plano, achados e acções correctivas? O
  primeiro está feito; o segundo é um módulo.
- «Conformidade documental» — a lacuna **K4** — é validar que um documento tem
  os campos que a lei exige. Qual lei, e que documentos?

**O que já existiria para aproveitar:** `documents` guarda ficheiros com hash
de integridade, `audit` tem a trilha imutável, e `hr` tem os contratos de
trabalho com o seu ciclo. A parte que falta é a de processo, não a de guarda.

## Comercial — o funil, que é caso diferente

O funil comercial (lead → oportunidade → proposta → contrato → cobrança)
**tem** requisitos: estão no documento de produto. Não está aqui por falta de
informação, mas por **decisão de recorte** — o ADR-036 cortou-o para reduzir
âmbito, e a 2026-09-07 confirmou-se que fica cortado.

Por isso o `commercial` passou de `parcial` a **`recorte-deliberado`** no
registo do frontend: «parcialmente implementado» diz que há trabalho a meio, e
não há. As sete entradas de menu do pipeline saíram pela mesma razão que as
outras quatro.

**Emitir não depende do funil**, e foi por isso que ficou de fora. Revertê-lo é
escrever um ADR e construir — não é destrancar nada.
