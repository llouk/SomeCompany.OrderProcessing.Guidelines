# Implementation plan

The skeleton (Phase 0) is done: it builds, runs and has tests. The phases below hand it to a delivery team. Work streams follow module boundaries, so they can run in parallel with few merge conflicts.

## Phase 0 — Skeleton ✅

- Module structure, contracts, one-way dependencies, boundary tests
- Ordering: aggregate + state machine (incl. `CanBeCancelled`), place / get / cancel under `/api/v1`
- In-memory database per module (Ordering orders; Pricing catalogue + tax rates)
- Redis idempotency store with atomic claim and `409` for concurrent duplicates
- Pricing pipeline; Inventory real HTTP adapter template + fake; Payments and Shipping fakes
- Contracts only (no implementation): messaging (`IOutbox`, `IInbox`, `IMessageTransport`, `IIntegrationEventHandler<T>`), integration events, carrier webhook (`CarrierWebhookPayload`, `ICarrierWebhookVerifier`), payment capture
- JWT auth, OpenTelemetry, live/ready probes, ProblemDetails, Dockerfile, CI, `docker-compose.yml`

## Phase 1 — Production-ready MVP

| Stream | Work | Done when |
|---|---|---|
| Persistence | Choose **PostgreSQL or SQL Server**; replace each module's in-memory DB with EF Core (`ordering`, `pricing` schemas, one `DbContext` each), migrations, row-version concurrency on `Order`; store the idempotency key under a unique index as a backstop | Integration tests on Testcontainers pass; no handler changed |
| Ordering | Request-hash check on idempotency keys (same key + different body → 422) | Tests cover replay, conflict, mismatch |
| Inventory | Confirm provider API (reservation + idempotency); finish `InventoryApiClient`; contract tests against a WireMock stub | Adapter passes contract tests; fake still used locally |
| Payments | Real provider adapter following the Inventory pattern: authorize, capture, void; map timeouts / circuit-open to `payments.unavailable`; on an unknown authorize outcome, void by orderId (provider idempotency key) | Sandbox end-to-end test passes |
| Shipping | Real carrier adapter: create + cancel shipment; confirm webhook format and signing scheme | Sandbox end-to-end test passes |
| Pricing | Real catalogue and tax source (or tax engine) behind the existing ports | Unit tests cover rounding, thresholds, countries |
| Platform | IaC for the [deployment view](architecture.md#8-deployment-view), identity provider config, Key Vault, managed Redis with replica, pipeline dev → test → prod with migrations, dashboards | One-click deploy; traces visible across providers |
| API | Integration tests with `WebApplicationFactory`; OpenAPI published | Every endpoint covered for success + main failures |

## Phase 2 — Reliability and full lifecycle (contracts already in code)

- **Outbox + inbox** tables in each module's database, background dispatcher with retries, in-process `IMessageTransport` (ADR-004, ADR-008; [api-flows.md §9](api-flows.md#9-phase-2--shipment-webhooks-and-outbox)).
- Move shipment creation and cancel-compensations onto the outbox.
- Stale **Pending** order sweeper (release reservation, reject).
- **Carrier webhook** `POST /api/v1/webhooks/shipping`: implement `ICarrierWebhookVerifier`, publish `ShipmentDispatched` / `ShipmentDelivered`, and have Ordering handle them → `MarkShipped` + `CaptureAsync`, `MarkDelivered` ([api-flows.md §9](api-flows.md#9-phase-2--shipment-webhooks-and-outbox)).
- Publish `OrderConfirmed` / `OrderRejected` / `OrderCancelled` for notifications and analytics.

## Phase 3 — Scale and evolve (driven by real need)

- Order listing / search via a read model.
- Back-office API with role-based policies; returns (adds a refund operation to `IPaymentService`).
- Rate limiting, multi-currency.
- Extract a module into its own service if load or team ownership requires it (ADR-001); add RabbitMQ behind `IMessageTransport` at that point (ADR-008).
- Further Redis uses only for a measured need: price cache, rate limiting, read cache (ADR-005).

## Team conventions

- A new module = new project with `Contracts/` + `XxxModule.cs`; register it in `Program.cs`; architecture tests must stay green.
- A supporting module never references Ordering. To report back, it publishes its own event from its `Contracts/`.
- A new external provider = adapter in its module + typed `HttpClient` + `AddStandardResilienceHandler()` + orderId as idempotency key.
- Expected failures return `Result`; never throw for business outcomes.
- A breaking API change gets a new version (`/api/v2`) alongside v1.
- Every decision that changes a rule here gets an ADR in `docs/decisions.md`.
