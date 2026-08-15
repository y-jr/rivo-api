# Agent Team Protocol

## Golden Rule

Every task must be classified before implementation.

Architectural review is required only when the change reaches the
corresponding change level.

The goal is to preserve architectural coherence without creating
unnecessary process overhead.

---

## Authority

### Architectural authority

The Architect has final authority over:

- architecture;
- module boundaries;
- dependency direction;
- architectural patterns;
- infrastructure decisions;
- ADRs.

### Security authority

The Security Engineer may block a change when it introduces a
material security vulnerability.

### Quality authority

The Code Reviewer may block a change when it introduces a serious
maintainability or code-quality problem.

### Testing authority

The Tester may block completion when required behavior is not
adequately demonstrated.

### Implementation authority

The Developer has authority over implementation details that remain
within established architectural and business constraints.

---

## Decision hierarchy

When decisions conflict:

1. Established ADR
2. Explicit architectural decision
3. Established project standard
4. Task requirements
5. Developer preference

A lower-level decision must not contradict a higher-level decision.

---

## Conflict resolution

If agents disagree:

1. Check the relevant ADR.
2. Check architecture documentation.
3. Check project standards.
4. Determine whether the disagreement is architectural, security,
   quality, testing, or implementation-related.
5. The responsible authority decides.
6. If the decision changes architecture, create or update an ADR.

The Architect does not override legitimate security blocking findings.

---

# Complexity Rule

Every new abstraction, dependency, pattern, or infrastructure component
must have a demonstrated reason.

"Future-proofing" alone is insufficient.

Prefer the simplest solution that satisfies the current requirements.

Do not introduce complexity solely because:

- it is considered a best practice;
- it may be useful someday;
- another project uses it;
- it improves hypothetical scalability;
- it makes the system theoretically more flexible.

---

# Change Scope

Agents must not modify unrelated areas.

A task should produce the smallest coherent change that solves the
requested problem.

If unrelated problems are discovered, report them separately unless
they directly prevent the task from being completed safely.

---

# Change Level Classification

Before implementation, classify the task.

## L0 — Mechanical

The change:

- does not alter behavior;
- does not alter architecture;
- does not alter contracts;
- does not introduce dependencies;
- does not modify security behavior.

Examples:

- formatting;
- typo;
- local variable rename;
- comments;
- obvious warning fixes;
- documentation corrections.

### Process

Developer may proceed directly.

No Architect review is required.

---

## L1 — Local Implementation

The architectural decision already exists and the implementation stays
inside established boundaries.

The change does not:

- introduce a new architectural dependency;
- change module boundaries;
- change established contracts;
- introduce new infrastructure;
- change security architecture.

Examples:

- implementing an existing use case;
- implementing an existing endpoint contract;
- adding validation already defined by the domain;
- adding tests;
- implementing an existing repository abstraction;
- adding a handler inside an existing module.

### Process

Developer may proceed directly.

Required tests must still be executed.

Security review is required only when the change affects security-sensitive
behavior.

Code review may be performed according to project policy.

---

## L2 — Significant Change

The change has meaningful impact but does not necessarily change the
architecture.

Examples:

- significant business behavior changes;
- important persistence changes;
- changes affecting multiple components;
- security-sensitive changes;
- changes with meaningful operational risk;
- changes affecting existing contracts.

### Process

The relevant specialist must review the change.

Depending on the nature of the change, this may include:

- Architect;
- Security;
- Tester;
- Code Reviewer.

The Architect is required when architectural constraints may be affected.

---

## L3 — Architectural Change

The change:

- changes module boundaries;
- changes dependency direction;
- introduces infrastructure;
- introduces a new architectural pattern;
- changes communication mechanisms;
- changes authentication architecture;
- changes persistence technology;
- changes established ADRs;
- changes deployment architecture.

### Process

Architect review is mandatory before implementation.

When appropriate:

1. Architect evaluates the proposal.
2. ADR is created or updated.
3. Relevant specialists review the decision.
4. Developer implements the approved decision.
5. Tests and specialist reviews are performed.
6. Architect performs final architectural review.

---

# Escalation Rule

The Developer must escalate when:

- an architectural decision is undefined;
- implementation would contradict an ADR;
- implementation requires crossing a module boundary;
- implementation requires a new dependency between modules;
- implementation introduces new infrastructure;
- implementation changes an established contract;
- implementation introduces meaningful security risk.

When uncertain between two levels, choose the higher level.

However, uncertainty alone must not be used to escalate trivial
implementation work.

The Developer should use existing ADRs, architecture documentation,
and project standards before escalating.

---

# Completion

A task is complete when:

### L0

- implementation is complete;
- required checks pass.

### L1

- implementation is complete;
- required tests pass;
- no unresolved architectural issue exists.

### L2

- required specialist reviews are complete;
- required tests pass;
- no blocking findings remain.

### L3

- Architect approved the architectural decision;
- ADR is updated when required;
- implementation is complete;
- required specialist reviews are complete;
- required tests pass;
- final architectural review passes.

No task is considered complete while a blocking finding remains unresolved.