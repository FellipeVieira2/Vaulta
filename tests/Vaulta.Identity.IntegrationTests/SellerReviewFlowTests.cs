using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vaulta.Identity.Contracts;
using Vaulta.Marketplace.Contracts;
using Vaulta.Marketplace.Domain;
using Vaulta.Marketplace.Infrastructure;
using Vaulta.Orders.Domain;
using Vaulta.Orders.Infrastructure;
using Vaulta.Reviews.Contracts;
using Vaulta.Reviews.Application;
using Vaulta.Reviews.Domain;
using Vaulta.Reviews.Infrastructure;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class SellerReviewFlowTests(ApiFixture fixture)
{
    [Fact]
    public async Task RatingsSeparateBuyerFromSellerAndAppearOnProfileAndListings()
    {
        using var api = fixture.Factory.CreateClient();
        var seller = await Register(api); var buyer = await Register(api); var stranger = await Register(api);
        var sale = OrderFor(buyer.User.Id, seller.User.Id); var purchase = OrderFor(seller.User.Id, buyer.User.Id);
        var pending = OrderFor(buyer.User.Id, seller.User.Id, false);
        var listing = Listing.Create(seller.User.Id, Guid.NewGuid(), Guid.NewGuid(), null, "NM", 100, null, DateTimeOffset.UtcNow);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var orders = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            orders.Orders.AddRange(sale, purchase, pending); await orders.SaveChangesAsync();
            var market = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            market.SellerProfiles.Add(SellerProfile.Enable(seller.User.Id, null, null, null, null, null, DateTimeOffset.UtcNow));
            market.Listings.Add(listing); await market.SaveChangesAsync();
        }
        api.DefaultRequestHeaders.Authorization = new("Bearer", stranger.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.PostAsJsonAsync("/api/v1/reviews", new CreateReviewRequest(sale.Id, 5, null))).StatusCode);
        api.DefaultRequestHeaders.Authorization = new("Bearer", buyer.AccessToken);
        Assert.Equal(HttpStatusCode.Conflict, (await api.PostAsJsonAsync("/api/v1/reviews", new CreateReviewRequest(pending.Id, 5, null))).StatusCode);
        var reviewedSeller = await api.PostAsJsonAsync("/api/v1/reviews", new CreateReviewRequest(sale.Id, 5, "Carta recebida"));
        reviewedSeller.EnsureSuccessStatusCode(); Assert.Equal("SELLER", (await reviewedSeller.Content.ReadFromJsonAsync<ReviewDto>())!.ReviewedRole);
        var reviewedBuyer = await api.PostAsJsonAsync("/api/v1/reviews", new CreateReviewRequest(purchase.Id, 1, null));
        reviewedBuyer.EnsureSuccessStatusCode(); Assert.Equal("BUYER", (await reviewedBuyer.Content.ReadFromJsonAsync<ReviewDto>())!.ReviewedRole);
        Assert.Equal(HttpStatusCode.Conflict, (await api.PostAsJsonAsync("/api/v1/reviews", new CreateReviewRequest(sale.Id, 4, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsJsonAsync("/api/v1/reviews", new { orderId = sale.Id, rating = 5, reviewedRole = "BUYER" })).StatusCode);
        var rating = await api.GetFromJsonAsync<SellerRatingDto>($"/api/v1/reviews/seller/{seller.User.Id}/rating");
        Assert.Equal(5m, rating!.AverageRating); Assert.Equal(1, rating.TotalReviews);
        var noReviews = await api.GetFromJsonAsync<SellerRatingDto>($"/api/v1/reviews/seller/{stranger.User.Id}/rating");
        Assert.Equal(0m, noReviews!.AverageRating); Assert.Equal(0, noReviews.TotalReviews);
        var detail = await api.GetFromJsonAsync<ListingDto>($"/api/v1/marketplace/listings/{listing.Id}");
        Assert.Equal(5m, detail!.SellerAverageRating); Assert.Equal(1, detail.SellerTotalReviews);
        var browse = await api.GetFromJsonAsync<ListingPageDto>($"/api/v1/marketplace/listings?sellerUserId={seller.User.Id}");
        Assert.Equal(detail.SellerAverageRating, Assert.Single(browse!.Items).SellerAverageRating);
        api.DefaultRequestHeaders.Authorization = new("Bearer", seller.AccessToken);
        var profile = await api.GetFromJsonAsync<SellerProfileDto>("/api/v1/me/seller");
        Assert.Equal(5m, profile!.AverageRating); Assert.Equal(1, profile.TotalReviews);
    }

    [Fact]
    public async Task ConcurrentReviewsReturnCreatedAndConflictWithoutDuplicateRating()
    {
        using var api = fixture.Factory.CreateClient(); var seller = await Register(api); var buyer = await Register(api);
        var order = OrderFor(buyer.User.Id, seller.User.Id);
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var orders = scope.ServiceProvider.GetRequiredService<OrdersDbContext>(); orders.Orders.Add(order); await orders.SaveChangesAsync();
        }
        var race = new ReviewRace();
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IReviewStore>();
            services.AddScoped<IReviewStore>(sp => new RacingReviewStore(sp.GetRequiredService<ReviewsDbContext>(), race));
        }));
        using var racingApi = factory.CreateClient(); racingApi.DefaultRequestHeaders.Authorization = new("Bearer", buyer.AccessToken);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => racingApi.PostAsJsonAsync("/api/v1/reviews", new CreateReviewRequest(order.Id, 4, null))));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Created); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(2, race.SaveAttempts);
        api.DefaultRequestHeaders.Authorization = new("Bearer", buyer.AccessToken);
        var rating = await api.GetFromJsonAsync<SellerRatingDto>($"/api/v1/reviews/seller/{seller.User.Id}/rating");
        Assert.Equal(1, rating!.TotalReviews);
    }

    [Fact]
    public async Task MigrationClassifiesLegacyRolesAndPreservesUnknownRows()
    {
        await using var postgres = await TestPostgresDatabase.Start();
        await using var db = new ReviewsDbContext(new DbContextOptionsBuilder<ReviewsDbContext>().UseNpgsql(postgres.ConnectionString).Options);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync("20260928221900_InitReviews");
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA orders; CREATE TABLE orders.orders (id uuid PRIMARY KEY, buyer_id uuid NOT NULL, seller_id uuid NOT NULL, status text NOT NULL);");
        var orderId = Guid.NewGuid(); var buyerId = Guid.NewGuid(); var sellerId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO orders.orders VALUES ({orderId}, {buyerId}, {sellerId}, 'delivered')");
        foreach (var row in new[] { (Reviewer: buyerId, Reviewed: sellerId, Rating: 5), (Reviewer: sellerId, Reviewed: buyerId, Rating: 1), (Reviewer: Guid.NewGuid(), Reviewed: sellerId, Rating: 1) })
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO reviews.reviews (id, order_id, reviewer_id, reviewed_user_id, rating, created_at) VALUES ({Guid.NewGuid()}, {orderId}, {row.Reviewer}, {row.Reviewed}, {row.Rating}, {DateTimeOffset.UtcNow})");
        await migrator.MigrateAsync();
        var roles = await db.Reviews.AsNoTracking().Select(x => x.ReviewedRole).ToListAsync();
        Assert.Equal(new[] { "BUYER", "SELLER", "UNKNOWN" }, roles.Order().ToArray());
        var rating = await new ReviewQueries(db).GetSellerRating(sellerId, default);
        Assert.Equal(5m, rating.AverageRating); Assert.Equal(1, rating.TotalReviews);
    }

    private static Order OrderFor(Guid buyer, Guid seller, bool delivered = true)
    {
        var now = DateTimeOffset.UtcNow;
        var order = Order.Create(buyer, seller, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "NM", 100, 8, null, null, null, null, null, null, null, null, now);
        if (delivered) { order.MarkAsPaid("pay_" + Guid.NewGuid().ToString("N"), now); order.MarkAsShipped("BR123", now); order.MarkAsDelivered(now); }
        return order;
    }
    private sealed class ReviewRace
    {
        private int _reads;
        public int SaveAttempts;
        private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task Wait(CancellationToken ct)
        {
            if (Interlocked.Increment(ref _reads) == 2) _bothRead.TrySetResult();
            await _bothRead.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        }
    }
    private sealed class RacingReviewStore(ReviewsDbContext db, ReviewRace race) : IReviewStore
    {
        private readonly ReviewStore _inner = new(db);
        public async Task<bool> HasReviewed(Guid orderId, Guid reviewerId, CancellationToken ct)
        {
            var found = await _inner.HasReviewed(orderId, reviewerId, ct);
            if (!found) await race.Wait(ct);
            return found;
        }
        public void Add(Review review) => _inner.Add(review);
        public Task Save(CancellationToken ct) { Interlocked.Increment(ref race.SaveAttempts); return _inner.Save(ct); }
    }
    private static async Task<AuthResponse> Register(HttpClient api)
    {
        var request = new RegisterRequest($"{Guid.NewGuid():N}@example.test", "Secure-Test-Password1!", "u" + Guid.NewGuid().ToString("N")[..20], "User");
        (await api.PostAsJsonAsync("/api/v1/auth/register", request)).EnsureSuccessStatusCode();
        var response = await api.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(request.Email, request.Password));
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }
}
