using Vaulta.Orders.Contracts;

namespace Vaulta.Orders.Application.Queries;

public sealed record GetOrderByIdQuery(Guid UserId, Guid OrderId);
public sealed record GetUserOrdersQuery(Guid UserId, string? Status, int Page, int PageSize);