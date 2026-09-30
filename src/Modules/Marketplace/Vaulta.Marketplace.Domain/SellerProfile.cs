using Vaulta.SharedKernel;

namespace Vaulta.Marketplace.Domain;

public sealed record SellerProfileEnabledDomainEvent(Guid Id, Guid UserId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record SellerProfileSuspendedDomainEvent(Guid Id, Guid UserId, string Reason, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class SellerProfile : AggregateRoot
{
    private SellerProfile() { }

    public Guid UserId { get; private set; }
    public string Status { get; private set; } = null!;
    public string? Bio { get; private set; }
    public string? Street { get; private set; }
    public string? City { get; private set; }
    public string? State { get; private set; }
    public string? ZipCode { get; private set; }
    public decimal AverageRating { get; private set; }
    public int TotalSales { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public Guid Version { get; private set; }

    public bool IsActive => Status == MarketplaceRules.ActiveStatus;

    public static SellerProfile Enable(Guid userId, string? bio, string? street, string? city, string? state, string? zipCode, DateTimeOffset now)
    {
        if (userId == Guid.Empty) throw new DomainException("User identifier is required.");
        var profile = new SellerProfile
        {
            UserId = userId,
            Status = MarketplaceRules.ActiveStatus,
            Bio = MarketplaceRules.Bio(bio),
            Street = MarketplaceRules.AddressField(street),
            City = MarketplaceRules.AddressField(city),
            State = MarketplaceRules.AddressField(state),
            ZipCode = MarketplaceRules.ZipCode(zipCode),
            AverageRating = 0m,
            TotalSales = 0,
            CreatedAt = now,
            UpdatedAt = now,
            Version = Guid.NewGuid()
        };
        profile.Raise(new SellerProfileEnabledDomainEvent(Guid.NewGuid(), userId, now));
        return profile;
    }

    public void Update(string? bio, string? street, string? city, string? state, string? zipCode, Guid expectedVersion, DateTimeOffset now)
    {
        EnsureActive();
        EnsureVersion(expectedVersion);
        Bio = MarketplaceRules.Bio(bio);
        Street = MarketplaceRules.AddressField(street);
        City = MarketplaceRules.AddressField(city);
        State = MarketplaceRules.AddressField(state);
        ZipCode = MarketplaceRules.ZipCode(zipCode);
        Touch(now);
    }

    public void Suspend(string reason, DateTimeOffset now)
    {
        if (!IsActive) throw new DomainException("Seller profile is already suspended.");
        Status = MarketplaceRules.SuspendedStatus;
        Touch(now);
        Raise(new SellerProfileSuspendedDomainEvent(Guid.NewGuid(), UserId, reason, now));
    }

    public void RecordSale(decimal rating, DateTimeOffset now)
    {
        EnsureActive();
        var total = TotalSales + 1;
        AverageRating = ((AverageRating * TotalSales) + rating) / total;
        TotalSales = total;
        Touch(now);
    }

    private void EnsureActive()
    {
        if (!IsActive) throw new DomainException("Suspended seller profiles cannot be modified.");
    }

    private void EnsureVersion(Guid expectedVersion)
    {
        if (Version != expectedVersion) throw new ConflictException("Seller profile changed. Reload and retry.");
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}