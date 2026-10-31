namespace OsuHitsound.Lanes;

/// <summary>Bisection over lists kept in time order (moments, notes, cues).</summary>
public static class Sorted
{
    /// <summary>The index of the first item whose time is at or after <paramref name="time"/> (Count when none is).</summary>
    public static int LowerBound<T>(IReadOnlyList<T> items, double time, Func<T, double> timeOf)
    {
        int lo = 0, hi = items.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (timeOf(items[mid]) < time) lo = mid + 1;
            else hi = mid;
        }

        return lo;
    }

    /// <summary>The index of the item nearest <paramref name="time"/> within <paramref name="tolerance"/> that passes <paramref name="filter"/>, or -1.</summary>
    public static int Nearest<T>(IReadOnlyList<T> items, double time, double tolerance, Func<T, double> timeOf, Func<T, bool>? filter = null)
    {
        int best = -1;
        double bestDistance = tolerance;
        for (int i = LowerBound(items, time - tolerance, timeOf); i < items.Count && timeOf(items[i]) <= time + tolerance; i++)
        {
            double d = Math.Abs(timeOf(items[i]) - time);
            if (d <= bestDistance && (filter is null || filter(items[i]))) (best, bestDistance) = (i, d);
        }

        return best;
    }
}
