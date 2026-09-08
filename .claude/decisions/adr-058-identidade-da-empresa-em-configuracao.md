# ADR-058: A identidade da empresa vive em configuração

## Status

Aceite (2026-09-07). Nova secção `Company` na configuração da aplicação.

## Context

O SAF-T AO exige um `Header` com a identidade de quem exporta: `CompanyID`,
`TaxRegistrationNumber`, `CompanyName`, `CompanyAddress`, `TaxEntity`,
`ProductCompanyTaxID`. É a única secção obrigatória do ficheiro — todas as
outras são `minOccurs="0"`.

**Esses dados não existem em lado nenhum do Rivo.** Procurados no código e na
configuração a 2026-09-07: zero ocorrências. O sistema conhece os NIF dos
clientes (`commercial`) e dos fornecedores (`procurement`), e não o seu
próprio.

Não é esquecimento — é que nada tinha precisado. As facturas emitidas até aqui
transportam a menção de software não certificado (ADR-036) e o nome de quem as
emite nunca foi lido de lado nenhum.

## Decision

A identidade da empresa é **configuração da instância**, numa secção `Company`,
e não uma entidade de domínio.

```
Company__Name=...
Company__TaxRegistrationNumber=...
Company__Address__StreetName=...
Company__Address__City=...
Company__Address__Country=AO
Company__TaxEntity=Global
```

### Porque configuração e não entidade

**Porque o Rivo é de uma empresa só** (ADR-003). Numa instalação sem
multi-tenancy, a empresa **é** o deployment: não há como haver duas, e uma
tabela com uma linha garantida seria uma entidade que nunca tem mais do que um
elemento e mesmo assim exige listagem, criação, e a pergunta «e se houver
duas?» em cada consulta.

**Porque muda com a instalação e não com o negócio.** O nome e o NIF da empresa
mudam quando se instala noutro cliente, não durante a operação. Isso é a
definição de configuração — a mesma razão pela qual a cadeia de ligação à base
de dados não é uma entidade.

**E porque falha cedo.** Um `.env` incompleto rebenta ao arrancar, com o nome
do campo em falta. Uma tabela vazia rebenta a meio de uma exportação, meses
depois, quando alguém finalmente precisa do ficheiro.

### O que isto não decide

**Não abre a porta à multi-tenancy.** Se o ADR-003 for revertido, esta decisão
tem de ser revertida com ele — e é por isso que fica escrita: a empresa em
configuração é consequência de haver uma só, não uma preferência independente.

### `SoftwareValidationNumber` fica em `"0"`

O XSD admite `"0"` para software não validado, e é o que o Rivo põe — não por
omissão de configuração, mas porque **é verdade**: o Rivo não está certificado
pela AGT (ADR-036).

O campo é configurável para o dia em que houver número. Pô-lo a `"0"` por
omissão e permitir sobrepor é o inverso de o exigir: quem não tem certificação
exporta na mesma, e quem a tiver diz-o.

## Consequences

### O arranque ganha uma verificação

A aplicação recusa arrancar sem `Company:Name` e `Company:TaxRegistrationNumber`.
As restantes são opcionais e saem do ficheiro quando ausentes — o XSD marca
poucas como obrigatórias.

⚠ **A primeira versão desta verificação não servia, e foi corrigida no mesmo
dia.** Só testava se os campos estavam vazios. O XSD exige que o NIF tenha
**10 a 15 caracteres** (`SAFAOAngolaVatNumber`), e um NIF de nove dígitos
levantava a aplicação sem uma queixa para depois produzir um ficheiro que a
AGT recusa — precisamente a falha tardia que esta decisão dizia querer evitar.
Uma verificação de arranque que não conhece o formato do que verifica não faz
o trabalho para que existe.

A correcção obrigou a trocar o `.Validate(predicado, mensagem)` por um
`IValidateOptions<CompanyOptions>`, porque o primeiro só aceita **mensagem
constante** — e a mensagem constante dizia «sem `Company:Name` e
`Company:TaxRegistrationNumber` não há como exportar», que passou a ser mentira
para quem tivesse preenchido os dois com um NIF curto. Numa falha de arranque a
mensagem é a única coisa que quem instala tem; apontar o campo errado manda
procurar onde não está.

⚠ **Isto quebra instâncias existentes** que actualizem sem acrescentar as duas
variáveis. É deliberado: uma instância sem identidade de empresa não pode
exportar SAF-T, e descobri-lo no arranque é melhor do que descobri-lo no dia da
entrega à AGT.

### O `.env.example` cresceu

Com as variáveis e um comentário a dizer o que cada uma é no ficheiro fiscal.
Quem instala não tem de ler o XSD para saber o que é `TaxEntity`.

### Não há ecrã

A identidade da empresa não se edita na aplicação, pela mesma razão que a
cadeia de ligação não se edita: muda com a instalação. Se um dia fizer sentido
editá-la, isso é uma decisão nova — e provavelmente vem acompanhada da
reversão do ADR-003.
