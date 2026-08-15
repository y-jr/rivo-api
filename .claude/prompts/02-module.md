# Prompt: Trabalho num Módulo

Usar ao iniciar ou continuar trabalho substancial num módulo.

## Passos

1. Confirmar que o módulo consta de
   [domain/domain-map.md](../domain/domain-map.md) e tem ficheiro em
   [modules/](../modules/).

   **Não inventar módulos.** Os 14 módulos estão fechados. Em particular,
   não recriar `organization`, `administration` nem `treasury` como módulos
   de topo — foram deliberadamente dissolvidos:
   - Departamento e Cargo → `hr`
   - Centro de Custo → `finance`
   - Tesouraria → contexto interno de `finance`
   - Administração, Dashboard, Portais, Analytics → read models e canais,
     sem ownership de dados

2. Ler o ficheiro do módulo por inteiro: responsabilidade, conceitos,
   ownership, dependências permitidas, contratos publicados, e a lista
   explícita de "não pode".

3. Ler [architecture/dependency-rules.md](../architecture/dependency-rules.md)
   e [architecture/module-boundaries.md](../architecture/module-boundaries.md).
   Confirmar direcção de camada e de módulo, e a tabela de dependências
   permitidas.

4. Ler [domain/business-rules.md](../domain/business-rules.md) para regras
   transversais que vinculam este módulo.

5. Verificar [domain/shared-concepts.md](../domain/shared-concepts.md) antes
   de assumir que algo pertence ao SharedKernel. Quase nada pertence.

6. Confirmar contra `docs/` — em particular
   `rivo-dados-integracoes-seguranca-v1.md` §1.2 para o esquema lógico
   daquele domínio.

7. Estruturar em API → Application → Domain, com Infrastructure a
   implementar ports.

8. Seguir [standards/](../standards/) — incluindo
   [security.md](../standards/security.md), que não é opcional.

9. Acrescentar testes ([standards/testing.md](../standards/testing.md)).

10. Actualizar [state/](../state/).

## Verificações específicas deste projecto

- O módulo tem passos de aprovação próprios? **Não deve.** Submete a
  `approval`.
- O módulo tem log de auditoria próprio? **Não deve.** Usa `audit`.
- O módulo guarda ficheiros por conta própria? **Não deve.** Usa
  `documents` com tabela de ligação própria (ADR-009).
- O módulo copia atributos de Colaborador? **Não deve.** Usa
  `ReferenciaColaborador` (ADR-010).
