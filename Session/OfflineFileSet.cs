using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Session;

/// <summary>
/// 线下同时打开的一个 VBO 文件。
/// </summary>
public sealed class OfflineFileEntry
{
    public required Guid Id { get; init; }

    public required string FilePath { get; set; }

    public required string DisplayName { get; set; }

    public required IReadOnlyList<VehicleSample> Samples { get; set; }

    public required IReadOnlyList<string> ColumnNames { get; set; }

    /// <summary>轨迹/曲线配色（#RRGGBB）。</summary>
    public required string ColorHex { get; set; }
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

    /// <summary>当前选中的线下文件（Dashboard 主读数）。未选时退回第一份。</summary>
    public Guid? SelectedId { get; private set; }

    public OfflineFileEntry? Primary =>
        Selected ?? (_files.Count > 0 ? _files[0] : null);

    public OfflineFileEntry? Selected
    {
        get
        {
            if (SelectedId is Guid id)
            {
                var hit = _files.FirstOrDefault(f => f.Id == id);
                if (hit is not null)
                    return hit;
            }

            return _files.Count > 0 ? _files[0] : null;
        }
    }

    public void Select(Guid id)
    {
        if (_files.Any(f => f.Id == id))
            SelectedId = id;
    }

    public OfflineFileEntry Add(
        string filePath,
        IReadOnlyList<VehicleSample> samples,
        IReadOnlyList<string> columnNames,
        string? displayName = null,
        string? preferredColorHex = null)
    {
        var color = NormalizeColor(preferredColorHex)
            ?? Palette[_colorIndex % Palette.Length];
        if (preferredColorHex is null)
            _colorIndex++;

        var entry = new OfflineFileEntry
        {
            Id = Guid.NewGuid(),
            FilePath = filePath,
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? System.IO.Path.GetFileName(filePath)
                : displayName.Trim(),
            Samples = samples,
            ColumnNames = columnNames,
            ColorHex = color
        };

        _files.Add(entry);
        SelectedId = entry.Id; // 新打开的文件成为当前选中
        return entry;
    }

    public bool SetColor(Guid id, string colorHex)
    {
        var entry = _files.FirstOrDefault(f => f.Id == id);
        var normalized = NormalizeColor(colorHex);
        if (entry is null || normalized is null)
            return false;
        entry.ColorHex = normalized;
        return true;
    }

    private static string? NormalizeColor(string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
            return null;
        var hex = colorHex.Trim();
        if (!hex.StartsWith('#'))
            hex = "#" + hex;
        if (hex.Length is not (7 or 9))
            return null;
        foreach (var ch in hex.AsSpan(1))
        {
            if (!char.IsAsciiHexDigit(ch))
                return null;
        }
        return hex.ToUpperInvariant();
    }

    public bool Remove(Guid id)
    {
        var removed = _files.RemoveAll(f => f.Id == id) > 0;
        if (removed && SelectedId == id)
            SelectedId = _files.Count > 0 ? _files[0].Id : null;
        return removed;
    }

    public bool UpdateData(
        Guid id,
        IReadOnlyList<VehicleSample> samples,
        IReadOnlyList<string> columnNames)
    {
        var entry = _files.FirstOrDefault(f => f.Id == id);
        if (entry is null)
            return false;
        entry.Samples = samples;
        entry.ColumnNames = columnNames;
        return true;
    }

    public bool UpdateIdentity(Guid id, string filePath, string? displayName = null)
    {
        var entry = _files.FirstOrDefault(f => f.Id == id);
        if (entry is null)
            return false;
        entry.FilePath = filePath;
        entry.DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? System.IO.Path.GetFileName(filePath)
            : displayName.Trim();
        return true;
    }

    public void Clear()
    {
        _files.Clear();
        _colorIndex = 0;
        SelectedId = null;
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
