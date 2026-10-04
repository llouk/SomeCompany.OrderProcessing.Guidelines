# API flows, contracts and response codes

Flows §5–§8 match the current code. §9 is Phase 2: its contracts are in code, its implementation is not.

## 1. Endpoints at a glance

Order endpoints require `Authorization: Bearer <JWT>`. The customer id is the token's `sub` claim, never the body. The API is versioned in the path (`/api/v1`). A breaking change becomes `/api/v2`, served alongside v1.

| Method | Route | Request | Success | Errors |
|---|---|---|---|---|
| `POST` | `/api/v1/orders` | `PlaceOrderRequest` + optional `Idempotency-Key` header | **201** `OrderResponse` + `Location` (status `Confirmed` **or** `Rejected`) | 400, 401, 409, 500 |
| `GET` | `/api/v1/orders/{id}` | — | **200** `OrderResponse` | 401, 404, 500 |
| `POST` | `/api/v1/orders/{id}/cancel` | `CancelOrderRequest` (optional) | **200** `OrderResponse` (status `Cancelled`) | 401, 404, 409, 500 |
| `POST` | `/api/v1/webhooks/shipping` *(Phase 2)* | Carrier payload + signature header | **200** | 400, 401 |
| `GET` | `/health/live` | — | **200** process is up | — |
| `GET` | `/health/ready` | — | **200** Redis reachable | **503** not ready |
| `GET` | `/openapi/v1.json` | — | **200** (Development only) | — |

## 2. Response codes

| Code | When | Body |
|---|---|---|
| **200** | Read or cancel succeeded | `OrderResponse` |
| **201** | Order **processed**. Check `status`: `Confirmed` or `Rejected` (out of stock, declined, provider unavailable). An idempotent replay also returns 201 with the original order. | `OrderResponse` |
| **400** | Malformed request (missing address field, lines or payment token; `Idempotency-Key` empty or over 64 characters), or business validation failed (no lines, quantity ≤ 0, duplicate SKU, unknown SKU, unsupported country). Nothing saved. | ProblemDetails |
| **401** | Missing, expired or invalid token (or invalid webhook signature) | empty |
| **404** | Order does not exist **or belongs to another customer** (no id probing) | ProblemDetails |
| **409** | `order.request_in_progress`: same `Idempotency-Key` still running. `order.invalid_transition`: e.g. cancelling a `Shipped`, `Rejected` or `Cancelled` order | ProblemDetails |
| **500** | Unexpected fault (bug, or Redis down when a key is sent). No stack trace. | ProblemDetails with `traceId` |
| **503** | `/health/ready` only: a dependency is unreachable | — |

## 3. HTTP contracts

```jsonc
// PlaceOrderRequest — POST /api/v1/orders
// Header (optional): Idempotency-Key: <client-generated unique string>
{
  "shippingAddress": { "line1": "1 Main St", "city": "Athens", "postalCode": "10552", "countryCode": "GR" },
  "lines": [ { "sku": "SKU-KEYBOARD", "quantity": 1 }, { "sku": "SKU-MOUSE", "quantity": 2 } ],
  "paymentToken": "tok_visa"            // provider-issued token, never card data; no prices in the request
}
```

```jsonc
// CancelOrderRequest — POST /api/v1/orders/{id}/cancel   (body optional)
{ "reason": "Changed my mind" }
```

```jsonc
// OrderResponse — 200 / 201
{
  "id": "01a108ee-58d0-7ee5-be08-52c63c99b066",
  "status": "Confirmed",                // Pending | Confirmed | Rejected | Cancelled | Shipped | Delivered
  "canCancel": true,                    // from the transition table: true only for Pending / Confirmed
  "lines": [
    { "sku": "SKU-KEYBOARD", "quantity": 1,
      "unitPrice": { "amount": 49.90, "currency": "EUR" }, "lineTotal": { "amount": 49.90, "currency": "EUR" } }
  ],
  "totals": {
    "subtotal": { "amount": 89.70, "currency": "EUR" },
    "charges":  [ { "code": "shipping", "amount": { "amount": 4.90, "currency": "EUR" } } ],
    "tax":      { "amount": 22.70, "currency": "EUR" },
    "total":    { "amount": 117.30, "currency": "EUR" }
  },
  "history": [                          // lifecycle tracking
    { "status": "Pending",   "at": "2026-10-05T10:00:00Z", "reason": "Order received" },
    { "status": "Confirmed", "at": "2026-10-05T10:00:01Z", "reason": "Stock reserved and payment authorized" }
  ]
}
```

```jsonc
// ProblemDetails (RFC 9457) — every 4xx/5xx with a body
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "order.invalid_transition",  // stable, machine-readable error code
  "status": 409,
  "detail": "An order cannot move from Shipped to Cancelled.",
  "traceId": "00-7e29d597b8342c29a0f8bc2bdd268811-840ff89f7b306a64-01"
}
```

## 4. Module contracts and internal ports

Cross-module contracts (public, in each module's `Contracts/`):

| Module | Contract | Operations | Errors returned |
|---|---|---|---|
| Pricing | `IPricingService` | `QuoteAsync(PriceQuoteRequest) → PriceQuote` | `pricing.no_lines`, `pricing.unknown_product`, `pricing.unsupported_country` |
| Inventory | `IInventoryService` | `ReserveAsync(orderId, StockItem[])`, `ReleaseAsync(orderId)` | `inventory.insufficient_stock`, `inventory.unavailable` |
| Payments | `IPaymentService` | `AuthorizeAsync(orderId, Money, token) → authorizationId`, `CaptureAsync(authorizationId, Money)`, `VoidAsync(authorizationId)` | `payments.declined` |
| Shipping | `IShippingService` | `CreateShipmentAsync(ShipmentRequest) → shipmentId`, `CancelShipmentAsync(shipmentId)` | provider errors |
| Shipping | Events | `ShipmentDispatched`, `ShipmentDelivered` (Phase 2) | — |
| Ordering | Events | `OrderConfirmed`, `OrderRejected`, `OrderCancelled` (Phase 2, external consumers) | — |

`orderId` is the idempotency key for every call to an external system.

Ports inside Ordering (internal):

| Port | Operations | Backed by |
|---|---|---|
| `IIdempotencyStore` | `TryClaimAsync → Claimed / InProgress / Completed(orderId)`, `CompleteAsync`, `ReleaseAsync` | **Redis** |
| `IOrderRepository` | `GetAsync`, `AddAsync`, `UpdateAsync` | In-memory DB → PostgreSQL / SQL Server (ADR-006) |

**Idempotency key states** (Redis key `ordering:idempotency:{customerId}:{key}`):

| State | Meaning | Response |
|---|---|---|
| absent → `SET NX "in-progress"` (TTL 2 min) succeeds | This request does the work | normal flow |
| `"in-progress"` | Same key, first request still running | **409** `order.request_in_progress` |
| `<orderId>` (TTL 24 h) | Already done | **201** with the original order |
| deleted | Request failed validation before an order existed | client may fix it and reuse the key |

## 5. Place order — happy path → 201 Confirmed

```mermaid
sequenceDiagram
    autonumber
    actor C as Client
    participant API as Ordering API<br/>POST /api/v1/orders
    participant H as PlaceOrderHandler
    participant R as IIdempotencyStore<br/>(Redis)
    participant DB as IOrderRepository<br/>(in-memory DB)
    participant PR as IPricingService
    participant INV as IInventoryService
    participant PAY as IPaymentService
    participant SH as IShippingService

    C->>API: POST /api/v1/orders<br/>Bearer JWT, Idempotency-Key: k1<br/>PlaceOrderRequest
    API->>API: Authenticate (else 401)<br/>Shape check (else 400)
    API->>H: PlaceOrderCommand(customerId = sub, ...)
    H->>R: TryClaimAsync(customerId, k1)<br/>SET NX "in-progress" TTL 2 min
    R-->>H: Claimed
    H->>H: Order.Create (else 400 order.*)
    H->>PR: QuoteAsync(countryCode, lines)
    PR-->>H: PriceQuote (else 400 pricing.*)
    H->>DB: AddAsync(order) — status Pending
    H->>R: CompleteAsync(customerId, k1, orderId)<br/>SET orderId TTL 24 h
    H->>INV: ReserveAsync(orderId, items)
    INV-->>H: Success
    H->>PAY: AuthorizeAsync(orderId, total, paymentToken)
    PAY-->>H: authorizationId
    H->>H: order.Confirm(authorizationId)
    H->>DB: UpdateAsync(order) — status Confirmed
    H->>SH: CreateShipmentAsync(orderId, address, items)
    SH-->>H: shipmentId
    H->>DB: UpdateAsync(order)
    H-->>API: Result (Order)
    API-->>C: 201 Created<br/>Location: /api/v1/orders/{id}<br/>OrderResponse { status: Confirmed, canCancel: true }
```

## 6. Place order — failure paths

```mermaid
sequenceDiagram
    autonumber
    actor C as Client
    participant H as Ordering<br/>POST /api/v1/orders
    participant R as IIdempotencyStore<br/>(Redis)
    participant DB as IOrderRepository
    participant PR as IPricingService
    participant INV as IInventoryService
    participant PAY as IPaymentService

    rect rgba(128,128,128,0.12)
    Note over C,PAY: A) Invalid request or unknown SKU / country → nothing saved, key released
    C->>H: POST (Idempotency-Key: k1, SKU-NOPE)
    H->>R: TryClaimAsync → Claimed
    H->>PR: QuoteAsync(...)
    PR-->>H: Error pricing.unknown_product
    H->>R: ReleaseAsync(customerId, k1)
    H-->>C: 400 ProblemDetails { title: "pricing.unknown_product" }
    end

    rect rgba(128,128,128,0.12)
    Note over C,PAY: B) Out of stock / inventory unavailable → Rejected, nothing charged
    C->>H: POST
    H->>DB: AddAsync(order) — Pending
    H->>INV: ReserveAsync(orderId, items)
    INV-->>H: Error inventory.insufficient_stock / unavailable
    H->>INV: ReleaseAsync(orderId)  ← outcome may be unknown after a timeout
    H->>DB: UpdateAsync — Rejected (reason in history)
    H-->>C: 201 OrderResponse { status: Rejected }
    end

    rect rgba(128,128,128,0.12)
    Note over C,PAY: C) Payment declined → stock released, Rejected
    C->>H: POST (paymentToken: tok_declined)
    H->>DB: AddAsync(order) — Pending
    H->>INV: ReserveAsync → Success
    H->>PAY: AuthorizeAsync → Error payments.declined
    H->>INV: ReleaseAsync(orderId)  ← compensation
    H->>DB: UpdateAsync — Rejected
    H-->>C: 201 OrderResponse { status: Rejected }
    end

    rect rgba(128,128,128,0.12)
    Note over C,PAY: D) Retry after completion → same order, no new side effects
    C->>H: POST (Idempotency-Key: k1, again)
    H->>R: TryClaimAsync(customerId, k1)
    R-->>H: Completed(orderId)
    H->>DB: GetAsync(orderId)
    H-->>C: 201 OrderResponse (same id, current status)
    end

    rect rgba(128,128,128,0.12)
    Note over C,PAY: E) Duplicate while the first request is still running → 409
    C->>H: POST (Idempotency-Key: k1, concurrent)
    H->>R: TryClaimAsync(customerId, k1)
    R-->>H: InProgress
    H-->>C: 409 ProblemDetails { title: "order.request_in_progress" }
    end
```

## 7. Get order

```mermaid
sequenceDiagram
    autonumber
    actor C as Client
    participant API as Ordering API<br/>GET /api/v1/orders/{id}
    participant H as GetOrderHandler
    participant DB as IOrderRepository

    C->>API: GET /api/v1/orders/{id}  Bearer JWT
    alt no / invalid token
        API-->>C: 401
    end
    API->>H: HandleAsync(id, customerId = sub)
    H->>DB: GetAsync(id)
    alt not found OR order.CustomerId ≠ sub
        H-->>API: Error order.not_found
        API-->>C: 404 ProblemDetails
    else owner
        H-->>API: Order
        API-->>C: 200 OrderResponse (status, canCancel, history)
    end
```

## 8. Cancel order

```mermaid
sequenceDiagram
    autonumber
    actor C as Client
    participant API as Ordering API<br/>POST /api/v1/orders/{id}/cancel
    participant H as CancelOrderHandler
    participant DB as IOrderRepository
    participant SH as IShippingService
    participant PAY as IPaymentService
    participant INV as IInventoryService

    C->>API: POST /api/v1/orders/{id}/cancel  { reason }
    API->>H: CancelOrderCommand(id, customerId = sub, reason)
    H->>DB: GetAsync(id)
    alt not found OR not owner
        API-->>C: 404 ProblemDetails order.not_found
    end
    H->>H: order.Cancel() — transition table decides<br/>(same rule as canCancel)
    alt status is Shipped / Delivered / Rejected / Cancelled
        API-->>C: 409 ProblemDetails order.invalid_transition
    end
    H->>DB: UpdateAsync — Cancelled
    Note over H,INV: Compensations (best effort in v1, outbox in Phase 2)
    opt has shipmentId
        H->>SH: CancelShipmentAsync(shipmentId)
    end
    opt has authorizationId
        H->>PAY: VoidAsync(authorizationId)
    end
    H->>INV: ReleaseAsync(orderId)
    API-->>C: 200 OrderResponse { status: Cancelled, canCancel: false }
```

## 9. Phase 2 — shipment webhooks and outbox

Phase 2 closes two gaps with one mechanism.
- **Reliable follow-up steps.** Confirmation and the "request shipment" step are saved together through the **outbox** and retried until delivered. Duplicates are skipped by the **inbox**.
- **Shipped / Delivered.** Shipping verifies and translates the carrier's webhook into **its own** event, and Ordering subscribes. The dependency stays Ordering → Shipping (ADR-003).

The outbox and inbox are tables in each module's database, not Redis or RabbitMQ (ADR-005, ADR-008). The transport is in-process; RabbitMQ implements `IMessageTransport` only when a module is extracted.

```mermaid
sequenceDiagram
    autonumber
    participant H as Ordering handlers
    participant ODB as Ordering DB<br/>(orders + outbox + inbox)
    participant D as Outbox dispatcher<br/>IMessageTransport (in-process)
    participant SH as Shipping module
    participant CA as Carrier
    participant PAY as IPaymentService

    Note over H,CA: A) Reliable "request shipment" after confirmation
    H->>ODB: ONE transaction: order → Confirmed + outbox OrderConfirmed
    loop until delivered
        D->>ODB: read unsent outbox rows
        D->>H: deliver OrderConfirmed (inbox: duplicate → skip)
    end
    H->>SH: CreateShipmentAsync(orderId = idempotency key)

    Note over H,CA: B) Carrier webhook → Shipped (Delivered works the same way)
    CA->>SH: POST /api/v1/webhooks/shipping + signature
    SH->>SH: ICarrierWebhookVerifier (else 401) · parse (else 400)<br/>inbox by carrier EventId (duplicate → 200 no-op)
    SH->>SH: outbox ShipmentDispatched (same transaction)
    SH-->>CA: 200
    D->>H: ShipmentDispatched → Ordering handler (at-least-once)
    H->>ODB: order.MarkShipped() (Confirmed → Shipped)
    H->>PAY: CaptureAsync(authorizationId, total)
```

**Webhook responses:** **200** for processed *and* duplicate events (so the carrier stops retrying), **401** bad signature, **400** unparsable payload. A 5xx makes the carrier retry, which is safe because of the inbox.

**Contracts already in code:** [Messaging.cs](../src/SomeCompany.OrderProcessing.SharedKernel/Messaging.cs) (`IIntegrationEvent`, `IOutbox`, `IMessageTransport`, `IInbox`, `IIntegrationEventHandler<T>`), [OrderIntegrationEvents.cs](../src/Modules/SomeCompany.OrderProcessing.Ordering/Contracts/OrderIntegrationEvents.cs), [ShipmentIntegrationEvents.cs](../src/Modules/SomeCompany.OrderProcessing.Shipping/Contracts/ShipmentIntegrationEvents.cs), [CarrierWebhook.cs](../src/Modules/SomeCompany.OrderProcessing.Shipping/CarrierWebhook.cs), `IPaymentService.CaptureAsync`, `Order.MarkShipped` / `MarkDelivered`.
