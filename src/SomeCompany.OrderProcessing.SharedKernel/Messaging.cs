namespace SomeCompany.OrderProcessing.SharedKernel;

// Messaging seams for Phase 2 (ADR-008). Contracts only: no implementation exists yet.
// Outbox and inbox are tables in the owning module's database; the transport is in-process
// until a module is extracted, then RabbitMQ implements IMessageTransport without touching the modules.

/// <summary>An event published to other modules or services. Versioned: only additive changes.</summary>
public interface IIntegrationEvent
{
    Guid MessageId { get; }

    DateTimeOffset OccurredAt { get; }
}

/// <summary>Stores an event in the module's outbox table, inside the same DB transaction as the state change.</summary>
public interface IOutbox
{
    void Enqueue(IIntegrationEvent integrationEvent);
}

/// <summary>Used only by the outbox dispatcher. In-process today; RabbitMQ when a module is extracted.</summary>
public interface IMessageTransport
{
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken ct);
}

/// <summary>Consumer-side deduplication: delivery is at-least-once, so each consumer records processed message ids.</summary>
public interface IInbox
{
    /// <returns>false when this consumer already processed the message.</returns>
    Task<bool> TryMarkProcessedAsync(Guid messageId, string consumer, CancellationToken ct);
}

/// <summary>Implemented by a module that reacts to another module's integration event.</summary>
public interface IIntegrationEventHandler<in TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken ct);
}
