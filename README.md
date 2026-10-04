# SomeCompany Order Processing Platform — Solution Skeleton

An implementation-ready skeleton of a **modular monolith** in .NET 10. It accepts an order only if it can be priced, the stock exists and the payment is authorized, then tracks it to delivery. External systems own stock, money and transport. It runs end to end with fake providers, so the delivery team starts from working code and agreed contracts.

## Architecture in five points

1. **Modular monolith**: one deployable with five modules: Ordering (core), Pricing, Inventory, Payments, Shipping.
2. **The compiler enforces the boundaries**: everything is `internal` except each module's `Contracts`. Dependencies point one way (Ordering → others), and architecture tests guard both rules.
3. **Ordering orchestrates placement**: idempotency claim → price → save → reserve stock → authorize payment → confirm → request shipment, with compensations on failure.
4. **Explicit lifecycle**: one transition table drives status changes, `canCancel` and the full history.
5. **Infrastructure behind ports**: an in-memory database per module (PostgreSQL or SQL Server during development), Redis for idempotency, and outbox/inbox and webhook contracts ready for Phase 2.

## Requirements coverage

| Requirement | Where it is met | Evidence |
|---|---|---|
| Create an order with one or more products | `POST /api/v1/orders` → [PlaceOrder.cs](src/Modules/SomeCompany.OrderProcessing.Ordering/Application/PlaceOrder.cs) | `PlaceOrderHandlerTests` |
| Retrieve an existing order | `GET /api/v1/orders/{id}` (owner only) | live run (200 / 404) |
| Cancel when permitted | `POST /api/v1/orders/{id}/cancel`; rule in the transition table, exposed as `canCancel` | `CancelOrderHandlerTests`, `OrderTests` |
| Track the lifecycle | `status` + `history` on every order; Shipped/Delivered via webhook contracts | `OrderTests` |
| Validate inventory before accepting | Reserve (atomic check + hold) before payment and confirmation | `PlaceOrderHandlerTests` |
| Pricing with taxes and charges | [Pricing pipeline](src/Modules/SomeCompany.OrderProcessing.Pricing/PricingService.cs): catalogue → charges → tax | `PricingServiceTests` |
| Integrate inventory, payment, shipping | Contracts per module; real HTTP adapter template (Inventory) + fakes; resilience pipeline | `ModuleBoundaryTests` |
| Non-functional requirements | [architecture.md §9](docs/architecture.md#9-non-functional-requirements) maps each to a mechanism and a next step | — |

## Known gaps (planned in the [implementation plan](docs/implementation-plan.md))

- Shipment creation and cancel-compensations are not retried, and a crash mid-placement leaves an order Pending → outbox + sweeper (Phase 2).
- In-memory databases lose data on restart and are single-instance → real database (Phase 1).
- Shipped/Delivered are modelled but not wired (webhook contracts only), and provider API shapes are unconfirmed.

## Read next

[Architecture](docs/architecture.md) → [API flows](docs/api-flows.md) → [Decisions (8 ADRs)](docs/decisions.md) → [Implementation plan](docs/implementation-plan.md) → [AI usage](docs/ai-usage.md)

## Run it

Requires the .NET 10 SDK and Docker (for Redis).

```bash
dotnet test                                   # 26 tests, no infrastructure needed

docker compose up -d                          # Redis on localhost:6379
cd src/SomeCompany.OrderProcessing.Api
dotnet user-jwts create --name customer-1     # prints a dev token
dotnet run                                    # http://localhost:5080
```

Then use [orders.http](src/SomeCompany.OrderProcessing.Api/orders.http) with the token:

| Try | Expect |
|---|---|
| Place the sample order | `201`, `Confirmed`, 89.70 + 4.90 shipping + 24% VAT = **117.30 EUR** |
| Repeat it with the same `Idempotency-Key` | Same order id |
| `paymentToken: "tok_declined"` | `201`, `Rejected`, stock released |
| Cancel twice | `200` (`canCancel: false`), then `409` |
