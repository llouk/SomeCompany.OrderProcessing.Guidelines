# Architecture Decision Records

Eight decisions, each with **Decision**, **Why**, **Trade-off** and **Revisit when**. Security and observability choices are in the [NFR table](architecture.md#9-non-functional-requirements).

---

## ADR-001 — Modular monolith, not microservices

**Decision:** One deployable (.NET 10 Web API) made of strongly separated modules.

**Why:** One team, one shared workflow (placing an order), and early-stage requirements. Microservices would add network failures, distributed transactions and per-service infrastructure before any of it pays off.

**Trade-off:** Modules scale and deploy together, so one bad deploy affects all of them.

**Revisit when:** a module needs independent scaling or release cadence, or gets its own team. Modules only talk through contracts, so extraction means swapping an in-process implementation for an HTTP or messaging client.

---

## ADR-002 — One project per module, `internal` by default, plain handlers

**Decision:** Each module is one class library. Only its `*.Contracts` namespace and its `XxxModule` registration class are public. Folders (`Domain`, `Application`, `Infrastructure`, `Api`) separate concerns inside it. Each use case is a plain handler class injected into its endpoint (no MediatR). Expected failures return `Result`/`Error`, mapped to HTTP in one place. `ModuleBoundaryTests` enforce the boundaries.

**Why:** The compiler becomes the boundary guard. It keeps 9 projects instead of ~25 with per-layer projects. There is no library, licensing question or reflection-based dispatch, and "go to definition" works.

**Trade-off:** Layering *inside* a module is a convention, not compiler-enforced.

**Revisit when:** a module's internal layering is regularly violated (split that module), or many cross-cutting behaviours appear (decorators first).

---

## ADR-003 — Dependencies point one way: Ordering → supporting modules

**Decision:** Ordering calls Pricing, Inventory, Payments and Shipping through their contracts. They never reference Ordering or each other. When a supporting module must report back (e.g. a shipment was dispatched), it publishes **its own** event from its own `Contracts`, and Ordering subscribes (ADR-008).

**Why:** There is one readable place for the end-to-end flow (`PlaceOrderHandler`), there are no cycles, and supporting modules stay reusable.

**Trade-off:** Ordering is the most coupled module. That is acceptable because it is the core domain.

---

## ADR-004 — Synchronous placement with compensation; rejection is an outcome

**Decision:** Placement runs step by step in one request: price → save Pending → reserve → authorize → confirm → request shipment. A failed step compensates the earlier ones and the order ends **Rejected**, returned as **201** with the reason in `history`. 4xx is only for invalid requests and conflicts.
- Provider **unavailable** (retries exhausted, circuit open, timeout) → mapped to `*.unavailable` → Rejected.
- A failed reservation is always followed by a release, because after a timeout the outcome is unknown and release is idempotent.
- If the order changed during placement (e.g. the customer cancelled it), confirmation is refused and the payment is voided.

**Why:** It is the simplest correct design: the flow fits on one screen and the client gets a definitive answer. A rejected order exists and can be retrieved, so an idempotent retry must return it. Failing fast avoids stuck orders and charges.

**Known gaps:** shipment creation and cancel-compensations are not retried; a crash mid-flow leaves an order **Pending**.

**Revisit when (Phase 2):** add the outbox (ADR-008) and a sweeper for stale Pending orders. If the flow grows, move to a saga that returns `202 Accepted`, where "unavailable" means "stay Pending and retry".

---

## ADR-005 — Reserve to validate stock; idempotency at every hop, keys in Redis

**Decision:** There is no separate "check stock" call: `ReserveAsync` checks and holds stock atomically. Every external call uses the **orderId** as its idempotency key. The client's `Idempotency-Key` is claimed in **Redis** (`SET NX`, 2 min while in progress, 24 h once linked to the order) through `IIdempotencyStore`. Redis is required configuration with no fallback, and it is used for nothing else. Key states are in [api-flows.md §4](api-flows.md#4-module-contracts-and-internal-ports).

**Why:** Check-then-reserve is a race (two buyers, one item). Idempotency makes retries safe, which is what allows the resilience handler to retry POSTs. Redis is shared by all instances, the claim is atomic, stale claims expire on their own, and the orders database stays unloaded. The key is linked right after the order is saved, so a retry never creates a second order. Redis is **not** used for the outbox/inbox, because it cannot share a transaction with the module's database.

**Trade-off:**
- If Redis is down, requests with a key fail (500) and readiness turns unhealthy. That is safer than a duplicate charge.
- Same key with a different body is not detected yet (Phase 1: request hash → 422).
- In the PoC, Redis outlives the in-memory database: after a restart, a stale key returns 404. With the real database, the key is also stored under a unique index.

---

## ADR-006 — Database per module: in-memory in the PoC, PostgreSQL or SQL Server during development

**Decision:** Each data-owning module has its own database, accessed only through its ports: Ordering (orders) and Pricing (catalogue, tax rates). **In the PoC they are in-memory. During development they will be changed to PostgreSQL or SQL Server** (company standard decides), using EF Core with one schema and `DbContext` per module, no cross-module joins or foreign keys, and **row-version optimistic concurrency** on `Order`. Inventory, Payments and Shipping own no business data.

**Why:** There is no database server to run, and the ports are already the seam, so switching touches only `Infrastructure/`. Schema-per-module keeps extraction cheap. EF Core's InMemory provider was rejected because it has no transactions or constraints and would give false confidence.

**Trade-off:** PoC data is lost on restart and not shared between instances, so run a single instance until the real database is in.

---

## ADR-007 — Pricing pipeline with a price snapshot on the order

**Decision:** Unit prices → subtotal → `IChargeRule`s → tax on (subtotal + charges) → total. The result is copied onto the order at placement. Prices never come from the client.

**Why:** It is open for extension (a new charge is a new class; a new tax source is a new `ITaxRateProvider`). The snapshot keeps accepted orders stable for invoicing and audit.

**Trade-off:** Tax on charges follows one simplified rule. Real rules come from a tax engine.

---

## ADR-008 — Outbox and inbox in the module's database; no broker in the PoC

**Decision:** The messaging contracts are **in code** without an implementation: `IIntegrationEvent`, `IOutbox`, `IMessageTransport`, `IInbox` and `IIntegrationEventHandler<T>` in SharedKernel; order events in `Ordering.Contracts`; shipment events in `Shipping.Contracts`. In Phase 2 the outbox and inbox become tables in each module's database, delivered by a background dispatcher over an **in-process** `IMessageTransport`. RabbitMQ implements that interface only when a module is extracted.

**Why:** An outbox only works in the same transaction as the business data, so a broker does not replace it, and inside one process a broker adds operations cost for no gain. Decoupling comes from the contracts. Each module publishes its own events, so dependencies stay one-way (ADR-003).

**Revisit when:** a module is extracted, an external system must subscribe, or competing consumers are needed. Then add managed RabbitMQ (or Azure Service Bus), and check the licence terms of any framework on top.
