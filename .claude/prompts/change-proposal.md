# Prompt — Propose a Change (pre-approval)

Use before implementing anything non-trivial.

> For the requested change, produce a **proposal** (do not implement yet):
> 1. **Restated requirement** (one sentence).
> 2. **End-to-end trace** of the current behavior (file:line; services + shard/client scope).
> 3. **Reusable components** to use (`OE.Shared` contracts/helpers; the correct service's repository —
>    not a new duplicate).
> 4. **Proposed approach** — smallest viable change, matching existing patterns
>    ([docs/ImplementationPatterns.md](../../docs/ImplementationPatterns.md)).
> 5. **Contract/impact** (`/impact-analysis`): shared-contract touch? additive vs breaking? siblings;
>    coordinated deploys; sharding effects.
> 6. **Risks & assumptions & unknowns** (tag each; cross-ref TechnicalDebt/ArchitectureRisks).
> 7. **Validation plan** (build + how behavior is checked; no test projects exist).
> 8. **Approval needed?** Flag explicitly if architectural (new pattern, contract change, new service,
>    sharding change) — and **wait** for approval before those.
