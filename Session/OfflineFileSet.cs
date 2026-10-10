using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Session;

/// <summary>
/// 线下同时打开的一个 VBO 文件。
/// </summary>
public sealed class OfflineFileEntry
{
    public required Guid Id { get; init; }

    public required string FilePath { get; init; }

    public required string DisplayName { get; init; }

    public required IReadOnlyList<VehicleSample> Samples { get; init; }

    public required IReadOnlyList<string> ColumnNames { get; init; }

    /// <summary>轨迹/曲线配色（#RRGGBB）。</summary>
    public required string ColorHex { get; init; }
}

/// <summary>
/// 多文件线下会话：打开 / 关闭 / 列举。
/// </summary>
public sealed class OfflineFileSet
{
    private static readonly string[] Palette =
    {
        "#E05252",
        "#C8A34A",
        "#4A9FD8",
        "#3FBF6F",
        "#C06AD8",
        "#E08A4A",
        "#5AC8C8",
        "#D8D84A"
    };

    private readonly List<OfflineFileEntry> _files = new();
    private int _colorIndex;

    public IReadOnlyList<OfflineFileEntry> Files => _files;

    public int Count => _files.Count;

    public OfflineFileEntry? Primary =>
        _files.Count > 0 ? _files[0] : null;

    public OfflineFileEntry Add(
        string filePath,
        IReadOnlyList<VehicleSample> samples,
        IReadOnlyList<string> columnNames)
    {
        var color = Palette[_colorIndex % Palette.Length];
        _colorIndex++;

        var entry = new OfflineFileEntry
        {
            Id = Guid.NewGuid(),
            FilePath = filePath,
            DisplayName = System.IO.Path.GetFileName(filePath),
            Samples = samples,
            ColumnNames = columnNames,
            ColorHex = color
        };

        _files.Add(entry);
        return entry;
    }

    public bool Remove(Guid id) =>
        _files.RemoveAll(f => f.Id == id) > 0;

    public void Clear()
    {
        _files.Clear();
        _colorIndex = 0;
    }

    /// <summary>所有已打开文件列名的并集。</summary>
    public List<string> UnionColumns()
    {
        return _files
            .SelectMany(f => f.ColumnNames)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
