namespace Duetto.Core.FileSystem;

public enum SortColumn
{
    Name,
    Size,
    Type,
    Modified,
}

public static class EntrySorter
{
    /// <summary>Directories always group before files; the column orders within each group.</summary>
    public static List<FileEntry> Sort(IEnumerable<FileEntry> entries, SortColumn column, bool ascending)
    {
        var grouped = entries.OrderByDescending(e => e.IsDirectory);
        IOrderedEnumerable<FileEntry> ordered = column switch
        {
            SortColumn.Size => Apply(grouped, e => e.SizeBytes),
            SortColumn.Type => Apply(grouped, e => e.TypeLabel, StringComparer.OrdinalIgnoreCase),
            SortColumn.Modified => Apply(grouped, e => e.ModifiedUtc),
            _ => Apply(grouped, e => e.Name, StringComparer.OrdinalIgnoreCase),
        };
        return ordered.ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

        IOrderedEnumerable<FileEntry> Apply<TKey>(
            IOrderedEnumerable<FileEntry> source, Func<FileEntry, TKey> key, IComparer<TKey>? comparer = null) =>
            ascending ? source.ThenBy(key, comparer) : source.ThenByDescending(key, comparer);
    }
}
