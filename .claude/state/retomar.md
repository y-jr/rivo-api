# Retomar — estado a 2026-09-08

Escrito para quem pega no trabalho noutra máquina. Diz onde as coisas estão,
o que morde, e o que decidir a seguir. Não substitui
[`project-state.md`](project-state.md) — complementa-o com o que só se aprende
a trabalhar.

## Como falar comigo

Duas instruções minhas que valem para toda a sessão:

- **Responder em português europeu.**
- **Fechar cada relatório com o passo seguinte**, não com o que foi feito.

## Antes de escrever a primeira linha

O que traz de máquina antiga e não está no git:

| Ficheiro | Porquê |
|---|---|
| `.env` | Está em `.gitignore`. Sem ele nada arranca — ver `.env.example` |
| `front/resources/` | Fontes da identidade visual, se não estiverem versionadas |

**Confirme o `.env` antes de subir a stack.** Em particular
`COMPANY_TAX_REGISTRATION_NUMBER`, que a aplicação recusa arrancar sem, e que
tem de ter **10 a 15 caracteres** (ADR-058).

## O que morde nesta máquina

**A stack sobe com os dois ficheiros, sempre:**

```bash
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d --build api
```

Sem o override não há porto publicado nem rede — e criar uma rede `proxy` à
mão para "resolver" isso parte a ligação à base de dados. Já aconteceu.

**A API responde em `http://127.0.0.1:5080`,** não `localhost`: em Windows o
`localhost` resolve para IPv6 antes de o contentor estar de pé, e dá um
`000` que parece a API em baixo.

**`npm run build` é o typecheck honesto do front.** `npx tsc --noEmit`
responde da cache e passa com erros a sério por baixo.

**A suite completa (`dotnet test Rivo.slnx`) demora mais de 10 minutos** e é
interrompida quando corre em segundo plano nesta configuração. Vale mais
correr os projectos afectados um a um.

## Onde está o SAF-T

É o trabalho das últimas sessões e o que está mais fresco.

| Secção | Estado |
|---|---|
| `Header` | Feito |
| `MasterFiles`: clientes, fornecedores, produtos, tabela de impostos | Feito |
| `MasterFiles`: `GeneralLedgerAccounts` | **Bloqueado** — ver decisões |
| `SourceDocuments`: facturas de venda, notas de crédito, recibos, compras | Feito |
| `MovementOfGoods`, `WorkingDocuments` | **Bloqueado** — ver decisões |
| `GeneralLedgerEntries` | Depende do plano de contas |

O ficheiro sai válido contra o XSD oficial, verificado com um validador
independente **sobre o que o servidor entrega** — não sobre o que o código
produz, porque a conversão para `iso-8859-1` acontece no meio.

⚠ **Não tem validade legal.** `SoftwareValidationNumber` a `"0"`, porque não
há certificação da AGT (ADR-036).

### O método que funcionou, e que vale a pena manter

1. **Ler o XSD antes de escrever**, e confirmar que o agregado tem o que a
   secção pede. Duas vezes anunciei uma secção como fácil sem o fazer e
   estava errado — `MovementOfGoods` não tem dados, os fornecedores não
   tinham morada.
2. **Validar contra o esquema, não conferir campos.** A ordem dos elementos é
   significativa (`xs:sequence`), e só o validador a apanha.
3. **Um teste com várias secções ao mesmo tempo.** Nenhum teste de secção
   isolada apanha uma troca de ordem entre secções — e apanhou-me duas vezes.
4. **Correr contra dados reais pela API.** Três defeitos só apareceram assim:
   a nota de crédito ausente do ficheiro, a morada vazia do consumidor final,
   e os códigos de imposto congelados.
5. **A omissão que o EF gera numa migração quase nunca serve.** Quatro vezes
   seguidas propôs cadeia vazia onde o XSD exige `minLength` 1. Ele satisfaz
   a base de dados, não o esquema.

## Decisões que esperam por si, não por técnica

Estão em [`pending-decisions.md`](pending-decisions.md). As que bloqueiam
trabalho agora:

**1. O plano de contas (PGC angolano).** O ADR-037 recusou inventá-lo sem
fonte primária. Sem ele não há `GeneralLedgerAccounts` nem
`GeneralLedgerEntries` — que é metade do que falta do SAF-T. É preciso o
documento oficial, não uma lista de segunda mão.

**2. O Rivo emite guias de remessa?** `MovementOfGoods` exige documentos
numerados, com série e cadeia de assinatura. O `StockMovement` de `inventory`
é um lançamento interno — não tem nada disso. Isto é funcionalidade de
produto, não mapeamento.

**3. O nó dos dois Admin.** O ADR-051, o ADR-054 e o ADR-057 interagem de uma
forma que nenhum deles descreve: a conta de arranque não tem Colaborador
associado, e sem ele não pode praticar actos. Há três saídas possíveis;
nenhuma foi decidida.

**4. O lado do movimento dos recibos no SAF-T.** A linha de liquidação sai
como `CreditAmount`. O XSD não fixa qual usar, e a convenção contabilística
angolana não está verificada aqui. Está marcado no código —
`ExportSaftFile.Pagamentos` — e **tem de ser confirmado antes de qualquer
entrega à AGT**.

## O que sei que está partido ou por acabar

- **Facturas emitidas antes de 2026-09-08 com códigos de imposto inválidos
  não são exportáveis, e não há correcção possível.** A linha congela o
  código na emissão. Na base de desenvolvimento isto bloqueia a exportação do
  ano inteiro; a janela a partir de 2026-09-08 exporta. São dados de teste.
- **`GET /finance/sales-invoices/chain` verifica a cadeia de integridade, mas
  ninguém o corre sozinho.** Falta a rotina agendada.
- **Nenhum ecrã do front foi visto por olhos.** Verifiquei 224 contratos por
  HTTP (`front/scripts/verificar-api.mjs`), e isso confirma que os dados
  chegam na forma certa — não que a interface se veja bem. Mexi em três
  formulários no último dia: fornecedores, emissão de factura e notas de
  crédito.

## O que eu faria a seguir

Por esta ordem:

1. **Abrir o front e percorrer um ciclo completo pelos ecrãs** — cliente,
   artigo, factura, nota de crédito, recibo, exportação. É a única
   verificação que falta e a única que eu não posso fazer.
2. Decidir 1 e 2 acima, que são o que desbloqueia o resto do SAF-T.
3. A rotina que corre a verificação da cadeia sem lho pedirem.

## Para retomar a conversa

Cole isto:

> Continuo o Rivo. Lê `.claude/state/retomar.md` e o `project-state.md`, e
> diz-me o que farias a seguir antes de mexeres em código.
