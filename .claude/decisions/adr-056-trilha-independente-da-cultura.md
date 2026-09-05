# ADR-056: A trilha de auditoria não depende da cultura do ambiente

## Status

Aceite (2026-09-05). Fixa a cultura em invariante no arranque da API, e põe
formato ISO explícito nas oito datas que a trilha escrevia sem ele.

Origem: encontrado ao levantar `projects` para testes de camada Application —
não era o que se procurava.

## Context

A trilha de auditoria monta o seu `NewValue` como JSON por **interpolação de
strings**:

```csharp
NewValue: $$"""{"endedOn":"{{endedOn}}","cost":{{cost}}}"""
```

Uma interpolação usa a **cultura corrente**. Isso tem duas consequências, e
ambas foram verificadas contra a base de dados em vez de deduzidas.

### As datas já estavam erradas

Enviou-se `2026-01-25` pela API e ficou gravado:

```json
{"endedOn":"01/25/2026","cost":1234.56}
```

`MM/dd/yyyy` — nem ISO, nem o formato que um leitor angolano espera. E num
registo mais antigo da mesma tabela estava `"endedOn":"08/11/2026"`, que é **11
de Agosto** e se lê como **8 de Novembro**.

Pior: o defeito estava **meio corrigido**. Quatro sítios já usavam
`{{x:yyyy-MM-dd}}`; oito não. Alguém encontrou o problema, resolveu-o onde
estava a olhar, e não o generalizou.

### Os decimais funcionavam por acidente

`"cost":1234.56` saiu com ponto — mas porque o contentor não define `LANG`, e
sem `LANG` o .NET usa cultura invariante. **Não porque o código o garantisse.**
Definir `LANG=pt_AO.UTF-8` no compose bastava para passar a gravar
`"cost":1234,56`, que parte o objecto em dois campos.

Havia **um** sítio com `InvariantCulture` explícito, em
`CloseMaintenance` — e só porque ali era preciso distinguir o nulo. Os outros
vinte e tal ficaram à sorte do ambiente.

### Porque isto importa mais do que parece

A trilha é **append-only** (BR-14). Um registo mal formatado não se corrige
depois — só se evita antes. E é material de retenção: um valor ambíguo numa
trilha de auditoria é exactamente o que ela existe para não ter.

## Decision

### Cultura invariante, fixada no arranque

```csharp
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
```

Fecha a classe inteira do problema decimal, em vez de a resolver sítio a sítio
em vinte ficheiros.

É seguro para tudo o resto: o que sai desta API é JSON, e o `System.Text.Json`
já serializa números e datas independentemente da cultura. **Nada aqui é
formatado para leitura humana** — a formatação para o utilizador acontece no
frontend, com a sua própria localização.

### Formato ISO explícito nas datas

Fixar a cultura **não** resolve as datas: o formato curto da cultura
invariante é `MM/dd/yyyy`, que continua ambíguo e continua a não ser ISO.

As oito passam a `{{x:yyyy-MM-dd}}`, alinhando com as quatro que já o faziam.

## Consequences

- Quatro testes novos em `Rivo.Fleet.Application.Tests`, que **forçam a cultura
  para `pt-PT`** antes de exercitar o caso de uso. Sem a correcção, o do fecho
  de manutenção falha — confirmado a revertê-la.
- Verificado contra a stack: a mesma chamada que gravava `"01/25/2026"` passa a
  gravar `"2026-01-25"`.

### O que não se corrige

**Os registos já gravados ficam como estão.** São append-only, e reescrevê-los
seria pior do que o defeito — a trilha deixaria de ser confiável por uma razão
maior. Quem os ler no futuro encontra `MM/dd/yyyy` até esta data e ISO depois,
e é isso que este ADR serve para explicar.

### Uma nota sobre como apareceu

Isto não foi encontrado a procurar defeitos. Apareceu ao ler o código de
`projects` para escolher o que testar, e a previsão que levou a abrir esse
ficheiro — «as horas e os custos acumulam-se por projecto» — estava errada:
`projects` não tem somas na camada Application, o agregado detém tudo.

Foi a segunda previsão errada seguida, depois da de `fleet`. As duas custaram
pouco porque a resposta foi ler o código antes de escrever testes — e a
segunda, ao falhar, deu isto.
