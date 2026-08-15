# documents — Documentos & Anexos

**Classificação:** generic domain / infraestrutura.

## Responsabilidade

Armazenamento de ficheiros, metadados e versionamento. Capacidade
transversal única, consumida por todos os contextos que precisam de anexar
ficheiros.

Corrige a fragmentação do protótipo, onde cada módulo reinventava "guardar
um ficheiro" (`generated_documents`, `document_templates`,
`client_documents`, `employee_documents`, mais colunas ad-hoc `file_url`,
`pdf_path`, `file_path`, `document_url`).

## Conceitos

| Conceito | Notas |
|---|---|
| Documento | categoria, ponteiro de storage, hash, versão, criado por |
| Modelo de Documento (Template) | usado por `hr` (declarações), `finance` (documentos fiscais) |

## Possui

O ficheiro, os metadados, o ponteiro de storage, o hash e o histórico de
versões.

**Não possui a ligação a registos de negócio.**

## Ligação a registos de negócio (ADR-009)

Cada contexto consumidor possui a sua **própria tabela de ligação**, com FKs
reais para o seu registo e para `documents.documento(id)`:

```
hr.colaborador_documento(colaborador_id → hr.colaborador,
                         documento_id  → documents.documento,
                         categoria)
```

Substitui a FK polimórfica (`entidade_tipo` + `entidade_id`) do desenho
inicial. Restaura integridade referencial e mantém a classificação e a
**retenção legal no contexto que as conhece** — `documents` não pode
saber genericamente qual o prazo legal de cada tipo de documento.

Direcção de dependência: o consumidor depende de `documents`; `documents`
não depende de ninguém.

## Depende de

`identity` (quem criou), `audit`. Object storage (infraestrutura).

## Consumido por

`hr`, `payroll`, `finance`, `commercial`, `procurement`, `fiscal`,
`projects`, `fleet`.

## Contratos publicados

- Armazenar documento e obter ponteiro/versão.
- Obter documento por id.
- Gerar documento a partir de template.

## Não pode

- Interpretar o conteúdo ou o significado de negócio de um documento.
- Conhecer prazos de retenção legal — isso é do contexto de origem.
- Referenciar registos de outros módulos.

## Regras de negócio

- Sem eliminação física de documentos sujeitos a retenção legal (BR-14).
- Retenção conhecida e imposta pelo contexto de origem (BR-15).
- Cifra em repouso (AES-256) para anexos.

## Perguntas em aberto

- Serviço concreto de object storage.
- Política de versionamento: todas as versões retidas, ou por categoria?

## Estado

**Implementado.** Upload, download, metadados por contrato, hash SHA-256 para
integridade, e a tabela de ligação em `hr` que valida o ADR-009.

Verificado em `scripts/verify-documents.ps1` (13 casos), incluindo que a **FK
entre schemas bloqueia a eliminação** de um documento ligado — a integridade
que a chave polimórfica não dava.

### Fora do implementado

| Omitido | Porquê |
|---|---|
| **Versionamento** | A política está em aberto ("todas as versões retidas, ou por categoria?"). Não se constrói metade de uma funcionalidade; acrescentar a coluna depois é migração trivial |
| **Templates** | `hr` e `finance` ainda não geram documentos. Seria especulativo |

### Armazenamento

Sistema de ficheiros sobre um volume, atrás do port `IDocumentStorage`.
Escolhido para não acrescentar um serviço à stack; trocar por S3 é
implementar a interface, e nada acima dela muda.

**Nomes em disco são o identificador**, repartido por dois níveis de
directório — evita colisões, travessia de caminhos, e a degradação de um
único directório com centenas de milhares de ficheiros.

### ⚠ Defeitos conhecidos

- **K11** — sem cifra em repouso, contra `standards/security.md`.
- **K12** — ficheiro órfão se a gravação de metadados falhar.

Ambos em [state/known-issues.md](../state/known-issues.md).
