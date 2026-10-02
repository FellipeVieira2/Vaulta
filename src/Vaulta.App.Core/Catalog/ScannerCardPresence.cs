namespace Vaulta.App.Core.Catalog;

/// <summary>
/// Bounded, local geometric prefilter, not TCG identification. Requires a centered
/// portrait rectangle with four coherent edges and interior detail. Uncertain
/// previews stay local; the user can still request a manual capture.
/// </summary>
public static class ScannerCardPresence
{
    public static bool IsPresent(ReadOnlySpan<byte> gray, int width, int height)
    {
        if (width is < 24 or > 256 || height is < 24 or > 256 || gray.Length != width * height) return false;
        var vertical = new double[width]; var horizontal = new double[height];
        for (var y = 3; y < height - 3; y++)
            for (var x = 3; x < width - 3; x++)
            {
                vertical[x] += Math.Abs(gray[y * width + x + 1] - gray[y * width + x - 1]) / (double)height;
                horizontal[y] += Math.Abs(gray[(y + 1) * width + x] - gray[(y - 1) * width + x]) / (double)width;
            }
        var columns = Peaks(vertical); var rows = Peaks(horizontal);
        foreach (var left in columns)
            foreach (var right in columns)
            {
                if (right <= left || left < width * .05 || right > width * .95) continue;
                foreach (var top in rows)
                    foreach (var bottom in rows)
                    {
                        if (bottom <= top || top < height * .05 || bottom > height * .95) continue;
                        var ratio = (right - left) / (double)(bottom - top);
                        var area = (right - left) * (bottom - top) / (double)(width * height);
                        if (ratio is < .58 or > .82 || area is < .20 or > .85
                            || Math.Abs((left + right) / 2d - width / 2d) > width * .15
                            || Math.Abs((top + bottom) / 2d - height / 2d) > height * .15) continue;
                        if (Edge(gray, width, left, top, bottom, true) && Edge(gray, width, right, top, bottom, true)
                            && Edge(gray, width, top, left, right, false) && Edge(gray, width, bottom, left, right, false)
                            && HasDetail(gray, width, left + 4, top + 4, right - 4, bottom - 4)) return true;
                    }
            }
        return false;
    }

    private static int[] Peaks(double[] strengths)
    {
        var peaks = new List<int>(8);
        foreach (var i in Enumerable.Range(3, strengths.Length - 6).OrderByDescending(i => strengths[i]))
        {
            if (strengths[i] < 8) break;
            if (peaks.Any(p => Math.Abs(p - i) <= 3)) continue;
            peaks.Add(i);
            if (peaks.Count == 8) break;
        }
        return peaks.ToArray();
    }

    private static bool Edge(ReadOnlySpan<byte> gray, int width, int fixedCoordinate, int start, int end, bool vertical)
    {
        // Ignore rounded corners and require a consistent contrast direction.
        // Random texture has strong gradients but lacks a coherent outer edge.
        var inset = Math.Max(2, (end - start) / 10);
        var positive = 0; var negative = 0; var count = 0;
        for (var along = start + inset; along < end - inset; along++)
        {
            var strongest = 0;
            for (var offset = -2; offset <= 2; offset++)
            {
                var at = fixedCoordinate + offset;
                var difference = vertical ? gray[along * width + at + 1] - gray[along * width + at - 1]
                    : gray[(at + 1) * width + along] - gray[(at - 1) * width + along];
                if (Math.Abs(difference) > Math.Abs(strongest)) strongest = difference;
            }
            if (strongest >= 16) positive++;
            if (strongest <= -16) negative++;
            count++;
        }
        return count > 0 && Math.Max(positive, negative) >= count * .75;
    }

    private static bool HasDetail(ReadOnlySpan<byte> gray, int width, int left, int top, int right, int bottom)
    {
        if (right <= left || bottom <= top) return false;
        long total = 0, squared = 0; var count = 0;
        for (var y = top; y < bottom; y++) for (var x = left; x < right; x++)
        { var value = gray[y * width + x]; total += value; squared += value * value; count++; }
        var mean = total / (double)count;
        return squared / (double)count - mean * mean >= 64;
    }
}
