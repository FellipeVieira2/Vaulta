namespace Vaulta.SharedKernel;

public static class Pagination
{
    public const int MaximumPageSize = 100;

    public static (int Page, int Size, int Offset) Normalize(int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaximumPageSize);
        var offset = (long)(page - 1) * pageSize;
        if (offset > int.MaxValue)
            throw new DomainException("Page exceeds the supported pagination range.");
        return (page, pageSize, (int)offset);
    }
}
