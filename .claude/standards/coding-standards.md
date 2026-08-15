# Padrões de Código

Aplicam-se a todos os módulos. Backend em C#/.NET.

## Princípios

- Respeitar as camadas e as fronteiras de módulo em
  [architecture/](../architecture/). Uma alteração conveniente que atravesse
  mal uma fronteira não é aceitável — procurar a costura correcta.
- **Menor alteração coerente.** Sem abstracções, padrões ou infraestrutura
  que a funcionalidade actual não exija.
- Sem generalização especulativa. Construir para o requisito à frente, não
  para um hipotético.
- Preferir código explícito a código esperto. A lógica de domínio deve ser
  legível por quem conhece o negócio, não só por quem conhece a framework.
- `Domain` sem framework: sem atributos de ORM, sem tipos HTTP, sem imports
  de infraestrutura.

## Distinguir sempre

Ao analisar ou propor algo, rotular explicitamente:

- **Facto** — está nos documentos, no schema ou no código.
- **Inferência** — deduzível com confiança razoável, não afirmado
  directamente.
- **Hipótese** — assumido por falta de informação; precisa de confirmação.
- **Decisão em aberto** — ainda por decidir.

Não inventar requisitos. Se faltar informação, dizer de que informação se
depende.

## Comentários

- Por omissão, nenhum. O código deve explicar-se por nomes e estrutura.
- Escrever comentário só quando captura um *porquê* não óbvio — uma
  restrição regulamentar, um workaround, uma invariante subtil — que não se
  deduz da leitura.
- Nunca comentários que descrevem *o que* o código faz nem que narram a
  tarefa em curso.

## Idioma

- Documentação em `.claude/` e `docs/`: português.
- Nomes de código: seguir [naming.md](naming.md).

## Consistência

Seguir [naming.md](naming.md), [error-handling.md](error-handling.md),
[persistence.md](persistence.md), [api.md](api.md) e
[security.md](security.md).

Quando for preciso um padrão que estes documentos não cobrem, levantar a
questão em vez de estabelecer silenciosamente uma convenção pontual.
