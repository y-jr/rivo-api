# Rivo — Claude Instructions

## Fonte de verdade

`.claude/docs/` é a **fonte de verdade** deste projecto. Os restantes ficheiros
em `.claude/` são a destilação vinculativa desses documentos para trabalho
diário.

| Documento | Papel |
|---|---|
| `docs/rivo-arquitetura-global-v1.md` | Domínio, ownership, fronteiras, dependências, decisões D1–D7 e resoluções R1–R5 |
| `docs/rivo-dados-integracoes-seguranca-v1.md` | Esquema de dados por domínio, integrações externas, segurança |
| `docs/rivo-fiscal-saft-ao-v1.md` | Contrato de completude de dados fiscais (SAF-T AO 1.01_01), com o XSD fixado em `docs/schemas/` |
| `docs/rivo-fiscal-regras-angola-v1.md` | ⚠ **Levantamento provisório, NÃO é fonte de verdade.** Regras fiscais recolhidas de fontes secundárias, por verificar profissionalmente. Não implementar |
| `docs/rivo-suite-descricao-modulos.md` | Documento de produto (14 módulos funcionais, perfis de acesso) |
| `docs/prompt-arquiteto-rivo (1).md` | Método de trabalho arquitectural esperado |

**Nunca editar `docs/`.** Se algo em `.claude/` contradisser `docs/`, `docs/`
prevalece — e o ficheiro em `.claude/` está errado e deve ser corrigido.

Se uma análise nova alterar uma conclusão de `docs/`, isso é uma decisão
arquitectural: regista-se como ADR em `decisions/`, não se reescreve `docs/`.

## Projecto

Rivo é uma plataforma de gestão empresarial integrada para PMEs em Angola,
construída de raiz (greenfield).

O protótipo existente e o **SGAP** não são código a refactorizar ou preservar.
Mas a distinção é importante:

- **Código do SGAP/protótipo:** não se preserva, não se migra, não é
  referência de arquitectura.
- **Requisitos do SGAP:** são **vinculativos**. O objectivo estratégico
  declarado é o Rivo absorver as capacidades do SGAP até este se tornar
  funcionalmente obsoleto. Requisitos funcionais e não funcionais do SGAP
  (segregação de funções, alçadas, auditoria append-only, RPO/RTO, MFA)
  fazem parte do âmbito do Rivo.

O protótipo é usado exclusivamente como **evidência de domínio** — que casos
de uso emergiram, que conceitos o negócio distingue, que erros já aconteceram.

## Arquitectura

- Monólito modular — um deployável, fronteiras internas fortes
  (ADR-001).
- Fronteiras por bounded context, domain-driven design.
- Arquitectura em camadas dentro de cada módulo.
- SQL Server, um schema lógico por domínio, ownership exclusivo de tabela
  (ADR-002 fixa o desenho; ADR-029 o motor).
- Empresa única, sem multi-tenancy na v1 (ADR-003).
- Backend em C#/.NET.

Detalhe em [architecture/](architecture/).

## Regra fundamental

Os módulos de negócio possuem as suas próprias regras de negócio.

Não introduzir acoplamento entre módulos sem justificação arquitectural.

## Camadas

```
API
→ Application
→ Domain

Infrastructure
→ implementa concerns técnicos exigidos por Application e Domain
```

## Shared Kernel

Manter o SharedKernel mínimo.

Não colocar lá conceitos de domínio apenas por serem usados por vários
módulos. Ver [domain/shared-concepts.md](domain/shared-concepts.md).

## Desenvolvimento

Antes de implementar uma funcionalidade:

1. Identificar o módulo dono ([domain/domain-map.md](domain/domain-map.md)).
2. Confirmar as regras de domínio desse módulo ([modules/](modules/)).
3. Confirmar as fronteiras de módulo
   ([architecture/module-boundaries.md](architecture/module-boundaries.md)).
4. Confirmar os ADRs existentes ([decisions/](decisions/)).
5. Confirmar contra `docs/` — é a fonte de verdade.
6. Implementar a menor alteração coerente.
7. Acrescentar testes.
8. Actualizar o estado do projecto ([state/](state/)).

## Disciplina arquitectural

Não inventar módulos, abstracções, padrões ou infraestrutura sem
justificação.

Quando uma decisão afecta a arquitectura, registá-la como ADR.

Distinguir sempre **Facto** (está nos documentos ou no código),
**Inferência** (deduzido com confiança), **Hipótese** (assumido por falta de
informação) e **Decisão em aberto**. Não inventar requisitos — se faltar
informação, dizer de que informação se depende.
