using Vaulta.SharedKernel;

namespace Vaulta.Collection.Domain;

public sealed class ItemOwnershipTransfer
{
    private ItemOwnershipTransfer() { }
    public Guid OrderId { get; private set; }
    public Guid ListingId { get; private set; }
    public Guid SellerId { get; private set; }
    public Guid BuyerId { get; private set; }
    public Guid SellerItemId { get; private set; }
    public Guid BuyerItemId { get; private set; }
    public decimal PriceBrl { get; private set; }
    public DateTimeOffset ReceivedAt { get; private set; }
    public static ItemOwnershipTransfer Create(Guid orderId, Guid listingId, Guid sellerId, Guid buyerId, Guid sellerItemId, Guid buyerItemId, decimal priceBrl, DateTimeOffset receivedAt)
    {
        if (new[] { orderId, listingId, sellerId, buyerId, sellerItemId, buyerItemId }.Any(x => x == Guid.Empty)
            || sellerId == buyerId || sellerItemId == buyerItemId || priceBrl <= 0 || decimal.Round(priceBrl, 2) != priceBrl)
            throw new DomainException("Invalid ownership transfer.");
        return new() { OrderId = orderId, ListingId = listingId, SellerId = sellerId, BuyerId = buyerId,
            SellerItemId = sellerItemId, BuyerItemId = buyerItemId, PriceBrl = priceBrl, ReceivedAt = receivedAt };
    }
}
