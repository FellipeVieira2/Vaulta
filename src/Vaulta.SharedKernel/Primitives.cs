namespace Vaulta.SharedKernel;

public interface IClock { DateTimeOffset UtcNow { get; } }
public interface IDomainEvent
{
    Guid Id { get; }
    DateTimeOffset OccurredAt { get; }
}
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _events = [];
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _events.AsReadOnly();
    protected void Raise(IDomainEvent @event) => _events.Add(@event);
    public void ClearDomainEvents() => _events.Clear();
}
public sealed class DomainException(string message) : Exception(message);
public sealed class ConflictException(string message) : Exception(message);
public sealed class NotFoundException(string message) : Exception(message);
public sealed class UnauthorizedException(string message = "Invalid credentials or session.") : Exception(message);
public sealed class ForbiddenException(string message) : Exception(message);

public sealed record Money
{
    public decimal Amount { get; }
    public string Currency { get; }
    public Money(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3 || !currency.All(char.IsAsciiLetter))
            throw new DomainException("Currency must be a three-letter code.");
        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }
}
