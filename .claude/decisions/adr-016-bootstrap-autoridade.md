# ADR-016: Bootstrap de Autoridade por Seed

## Status

Aceite (2026-08-10)

## Context

ADR-014 e ADR-015 deixaram um problema de arranque com a mesma forma nos dois
casos:

- O seed de perfis **não cria utilizadores**, logo ninguém nasce com
  permissão para atribuir perfis.
- Ninguém ocupa um Cargo com autoridade de aprovação, logo não há quem aprove
  a primeira atribuição desses Cargos.

Em ambos, o sistema exige uma autoridade que só ele próprio pode conceder.
Num ambiente novo, isso torna-o inutilizável sem intervenção manual na base
de dados — que era exactamente o passo documentado como contorno.

## Requirements

- **Facto** — Um ambiente novo tem de ficar utilizável sem `INSERT` manual.
- **Facto** — Passwords não podem estar no código-fonte.
- **Facto** — Credenciais vêm de configuração ou gestão de segredos.
- **Facto** — O seed corre depois das migrações.
- **Facto** — Alterações de autoridade são auditadas (BR-13).

## Constraints

- Sem endpoint público para criar o primeiro administrador.
- Sem mecanismo distinto para Admin e para decisores.
- Sem abstracções adicionais nem serviços externos.
- Sem transformar o seed em regra de negócio.

## Alternatives

1. **Seed a partir de configuração** (escolhida).
2. Endpoint público de "primeiro arranque", desactivado depois.
3. Comando de CLI dedicado.
4. Manter o `INSERT` manual documentado.

A opção 2 foi rejeitada: um endpoint que cria administradores é superfície de
ataque permanente, e "desactivado depois" depende de estado que pode falhar.

A opção 3 acrescenta um executável e um ciclo de vida próprios para resolver
algo que a configuração já resolve.

A opção 4 é o que existia. Não escala e é fácil de fazer mal.

## Decision

**O bootstrap inicial de autoridade é realizado por seed controlado e
idempotente. O mecanismo de bootstrap não participa das regras normais de
autorização.**

### Ordem de execução

```
Migrations → AccessProfileSeeder → BootstrapUserSeeder
             (perfis + permissões)  (utilizadores + associações)
```

Obrigatória: sem schema não há onde semear; sem perfis não há a que associar.

### Um mecanismo, não dois

Admin e decisores são **entradas da mesma lista** `Bootstrap:Users`, com
perfis diferentes. Não há caminho especial para nenhum deles.

### Configuração

| Chave | Origem |
|---|---|
| `Bootstrap:Users:N:Email` | `.env` → `docker-compose.yml` |
| `Bootstrap:Users:N:Password` | idem — **nunca no repositório** |
| `Bootstrap:Users:N:Profiles:M` | `docker-compose.yml` |

Lista vazia é estado válido: não semeia ninguém.

### Idempotência

- Utilizador procurado por e-mail antes de criar.
- Pertença a perfil verificada antes de atribuir.
- **Contas existentes nunca são alteradas, incluindo a password.** Repô-la a
  cada arranque sobrescreveria uma credencial que o utilizador pudesse ter
  mudado.

### Falha em vez de silêncio

O arranque falha, em vez de deixar o ambiente sem administrador, quando:

- a configuração é inválida (`ValidateOnStart`);
- um perfil configurado não existe no catálogo;
- a password não cumpre a política.

### Não é regra de negócio

O seeder usa `UserManager`/`RoleManager` directamente, não os casos de uso.
É deliberado: existe para o momento em que ainda não há ninguém com
autoridade para conceder autoridade.

Isto **não é excepção às regras de autorização** — é o passo anterior a elas
poderem ser aplicadas. Depois do bootstrap, tudo passa pelas regras de
ADR-014.

## Consequences

Facilita:

- `docker compose up` deixa um ambiente novo utilizável.
- Acrescentar um utilizador inicial é configuração, não código.
- Sem superfície de ataque adicional.

Dificulta / exige:

- As credenciais iniciais têm de ser geridas fora do repositório em cada
  ambiente.
- Quem tiver acesso à configuração do ambiente pode criar administradores.
  É inerente a qualquer bootstrap; mitiga-se com gestão de segredos e
  auditoria do acesso à configuração.

## Risks

### R1 — Autoridade por Cargo não é semeável

O seed atribui **apenas Perfis de Acesso**. A autoridade de decisão de
ADR-015 vem do **Cargo**, que pertence a `hr` — módulo que não existe. Não há
tabela onde escrever.

**O problema de arranque de ADR-015 §R2 permanece em aberto.** Resolve-se
estendendo este mesmo seeder quando `hr` for implementado. Registado em
[pending-decisions](../state/pending-decisions.md).

> **Actualização de 2026-08-15 — o bloqueio mudou de módulo.** `hr` foi
> entretanto implementado e a tabela existe. A decisão deste ADR não muda, mas
> a razão do impedimento sim: já não falta onde escrever, falta **quem
> decida**. Criar um Cargo com autoridade exige decisão de `approval`, e é
> isso que faz a atribuição devolver `501`. O seeder continua por estender —
> agora à espera de `approval`, não de `hr`.

### R2 — Bootstrap só corre em Development

`InitialiseIdentityModuleAsync` está gated a `Development`, junto com as
migrações. Em produção, nem migrações nem seed correm — continua a ser a
decisão pendente sobre inicialização em produção, e este ADR não a altera.

### R3 — Password fraca na configuração

A política do Identity é aplicada, logo uma password fraca faz falhar o
arranque. Mas uma password forte e conhecida por muitos continua a ser risco
operacional, não técnico.

## Revisit When

- `hr` for implementado, para estender o seed à atribuição de Cargo (R1).
- A inicialização em produção for decidida (R2).
- For necessário rotacionar credenciais iniciais de forma automatizada.

## Related

- [ADR-014](adr-014-rbac-permissoes.md), [ADR-015](adr-015-atribuicao-cargo.md)
- [modules/identity.md](../modules/identity.md)
