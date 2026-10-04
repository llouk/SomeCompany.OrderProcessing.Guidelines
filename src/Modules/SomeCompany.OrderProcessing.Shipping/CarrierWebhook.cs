namespace SomeCompany.OrderProcessing.Shipping;

// Inbound carrier webhook (Phase 2): POST /api/v1/webhooks/shipping. Contracts only, no endpoint mapped yet.
// The payload shape is an assumption to confirm with the carrier. It stays internal to Shipping (anti-corruption
// layer): only the translated ShipmentDispatched / ShipmentDelivered events leave the module.

/// <summary>The carrier's notification as received.</summary>
internal sealed record CarrierWebhookPayload(
    string EventId,
    string ShipmentId,
    string Status,
    DateTimeOffset OccurredAt,
    string? TrackingUrl);

/// <summary>
/// Verifies the carrier's signature (e.g. HMAC-SHA256 of the raw body with a shared secret from Key Vault)
/// before anything is parsed. Unverified requests are rejected with 401.
/// </summary>
internal interface ICarrierWebhookVerifier
{
    bool IsAuthentic(ReadOnlySpan<byte> rawBody, string? signatureHeader);
}
