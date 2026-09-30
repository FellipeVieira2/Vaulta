namespace Vaulta.Payments.Domain;

public sealed class WebhookEvent
{
    private WebhookEvent() { }

    public Guid Id { get; private set; }
    public string EventType { get; private set; } = null!;
    public string AsaasPaymentId { get; private set; } = null!;
    public string? Payload { get; private set; }
    public bool Processed { get; private set; }
    public string? Error { get; private set; }
    public int RetryCount { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }

    public static WebhookEvent Receive(string eventType, string asaasPaymentId, string? payload, DateTimeOffset receivedAt)
    {
        return new WebhookEvent
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            AsaasPaymentId = asaasPaymentId,
            Payload = payload,
            Processed = false,
            ReceivedAt = receivedAt
        };
    }

    public void MarkProcessed()
    {
        Processed = true;
        ProcessedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string error)
    {
        Error = error;
        RetryCount++;
    }
}