using Vaulta.SharedKernel;

namespace Vaulta.Reviews.Domain;

public static class ReviewRules
{
    public const int MinRating = 1;
    public const int MaxRating = 5;
    public const int MaxCommentLength = 2000;

    public static string? Comment(string? comment)
    {
        if (comment is null) return null;
        var trimmed = comment.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length > MaxCommentLength)
            throw new DomainException($"Comment must be at most {MaxCommentLength} characters.");
        return trimmed;
    }

    public static int ValidateRating(int rating)
    {
        if (rating < MinRating || rating > MaxRating)
            throw new DomainException($"Rating must be between {MinRating} and {MaxRating}.");
        return rating;
    }
}