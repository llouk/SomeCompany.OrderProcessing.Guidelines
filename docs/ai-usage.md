# AI-Assisted Delivery: Approach and Governance

## 1. Operating model

I used **Claude Code** (Anthropic) as an engineering assistant working directly in the repository. Responsibilities were split deliberately:

| Responsibility | Owner |
|---|---|
| Problem framing, scope, architectural style, module boundaries | **Me (architect)** |
| Technology choices and trade-offs (Redis, persistence strategy, messaging approach) | **Me**, informed by AI analysis |
| Drafting code, tests, diagrams and documentation | AI, under my directives |
| Review, acceptance and correction of every artifact | **Me** |
| Verification (build, tests, live runs, doc-to-code consistency) | AI executed, I set the acceptance criteria |

The rule I applied throughout: **AI accelerates production; every architectural decision is mine and recorded in an ADR, and nothing is accepted until it is verified.**

## 2. How I directed the work

I worked in short iterations, each with an explicit directive and acceptance criteria.

| # | My directive | Outcome |
|---|---|---|
| 1 | Propose a deliverable set suited to a 4–6 h timebox for a delivery-team handover | Deliverables agreed: running skeleton, ADRs, flow diagrams, implementation plan |
| 2 | Build it **simple and minimal**: every artifact must be defensible in a review | Modular monolith, core Ordering slice, tests, CI, container, first docs |
| 3 | Assess Redis and RabbitMQ for inbox/outbox and future decoupling; document flows with contracts, endpoints and response codes | Analysis recorded (ADR-008); per-flow sequence diagrams |
| 4 | **Constraints:** Redis for idempotency; one in-memory database per module, documented for migration to PostgreSQL or SQL Server; interfaces and contracts in code; implementation logic only where necessary | Redis idempotency store, per-module storage ports, messaging and integration-event contracts |
| 5 | Run a gap analysis against the assignment and close every finding | Webhook and payment-capture contracts, deployment view, order model, API versioning, readiness probe |
| 6 | Run a **requirements-based review of code and docs together** | 4 defects found and fixed, with regression tests (§4) |
| 7 | Reduce reading load without losing substance; **keep the full API response example** | ADRs 13 → 8, one-page README, merged Phase 2 flows, unused contract removed, 9 projects |

## 3. Decisions I made and the AI's input

| Decision | AI input | My call and rationale |
|---|---|---|
| **Modular monolith** over microservices | Proposed | Accepted: one team, one transactional workflow, early-stage requirements (ADR-001) |
| **Redis for idempotency** | Initially advised against introducing Redis | **Overrode.** A shared, atomic, self-expiring store is the right tool for idempotency across instances. I kept the AI's point that Redis must *not* hold the outbox/inbox, because it cannot share a transaction with the business data (ADR-005) |
| **No message broker yet** | Advised against RabbitMQ in the PoC | Accepted, but the seam (`IMessageTransport`) is in code so RabbitMQ can be introduced when a module is extracted (ADR-008) |
| **Database per module, in-memory first** | — | My directive: fix data ownership now, add infrastructure during development behind the same ports (ADR-006) |
| **Contracts before implementations** | — | My directive: the delivery team builds against agreed seams, and the PoC holds no speculative logic |
| **Messaging interfaces kept** | Proposed removing two as speculative, then reversed once the webhook flow gave them a concrete use | Accepted the reversal: they carry the carrier-webhook flow and keep dependencies one-way (ADR-003) |
| **Scope of simplification** | Proposed removing the JSON response example | **Rejected:** the full response contract is what a consuming team needs first |

## 4. Quality control

AI output that compiles and reads well is not evidence of correctness, so I required three independent checks.

- **Automated:** 0-warning build, 26 tests covering the domain state machine, placement, cancellation (including a concurrent-cancel race), idempotency, pricing, and architecture boundary rules.
- **Behavioural:** live runs against a real Redis. Checked: 401 without a token, pricing totals, idempotent replay, 409 for a concurrent duplicate, rejection paths, cancellation rules, 404 for another customer's order, readiness 503 when Redis is down.
- **Adversarial review:** a requirements-driven review found **4 defects where the code contradicted the documentation**:
  1. a provider timeout surfaced as a 500 instead of a rejected order;
  2. stock was not released when a reservation outcome was unknown;
  3. a concurrent cancellation could leave a payment authorized;
  4. a blank address field caused a 500.

  Each one was fixed and is covered by a test or a live check.

The fourth point is the main lesson: **AI-generated artifacts need the same adversarial review as a junior engineer's work, especially at failure boundaries.**

## 5. Timebox allocation

| Depth: built and tested | Breadth: contracts and documentation | Deliberately excluded |
|---|---|---|
| Module boundaries + architecture tests; order aggregate and state machine; placement, cancellation and idempotency flows; pricing pipeline; one real provider adapter as the reference pattern | Payments and Shipping adapters (fakes), outbox/inbox, carrier webhooks, payment capture, deployment view, production databases | Message broker, event sourcing, IaC, back-office, returns, multi-currency |

## 6. Known limitations I accept

- **Rejecting on provider unavailability** is a v1 simplification (ADR-004). For high-value orders, "keep Pending and retry" may serve the business better, and it is listed as an open question.
- **Tax on shipping charges** follows one simplified rule. Production tax logic belongs to a tax engine or a domain expert.
- **Provider API shapes** (reservations, carrier webhooks) are assumptions until the providers confirm them.
- **The fake adapters** are in-memory and intended only for local development and demos.

## 7. Recommended AI guardrails for the delivery team

1. **ADRs are the context.** Give the assistant `docs/decisions.md` with every task, so its suggestions stay within agreed decisions.
2. **Architecture tests are the fence.** AI-generated code must keep `ModuleBoundaryTests` green; a boundary violation fails the build, not a review.
3. **Tests before acceptance.** Every AI-produced change ships with tests, and failure paths are reviewed by a human.
4. **Decisions stay human.** The assistant may propose a new dependency or pattern, but it enters the codebase only through an ADR.
