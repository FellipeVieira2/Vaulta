using Vaulta.Reviews.Contracts;

namespace Vaulta.Reviews.Application.Commands;

public sealed record CreateReviewCommand(Guid UserId, CreateReviewRequest Request);