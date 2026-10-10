using System.Text;

namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>
/// In-process maths channel definitions (lightweight until Settings persists them).
/// </summary>
public sealed class MathsChannelStore
{
    public static MathsChannelStore Instance { get; } = new();

    private readonly List<MathsChannelDefinition> _defs = new();

    public IReadOnlyList<MathsChannelDefinition> Definitions => _defs;

    public event EventHandler? Changed;

    public void Clear()
    {
        if (_defs.Count == 0)
            return;
        _defs.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Replace all definitions atomically (used by Maths Channels dialog OK).</summary>
    public bool TryReplaceAll(IEnumerable<MathsChannelDefinition> definitions, out string error)
    {
        error = "";
        var list = definitions.ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in list)
        {
            if (string.IsNullOrWhiteSpace(definition.Id))
            {
                error = "Channel Id cannot be empty.";
                return false;
            }

            if (!IsValidId(definition.Id))
            {
                error = $"Invalid Id '{definition.Id}' (letters/digits/_ only, not starting with a digit).";
                return false;
            }

            if (string.IsNullOrWhiteSpace(definition.Expression))
            {
                error = $"Expression for '{definition.Id}' cannot be empty.";
                return false;
            }

            if (!seen.Add(definition.Id))
            {
                error = $"Duplicate channel Id: {definition.Id}";
                return false;
            }

            if (!MathsExpression.TryEvaluate(definition.Expression, _ => 0, out _, out var evalError))
            {
                error = $"'{definition.Id}': {evalError ?? "invalid expression"}";
                return false;
            }
        }

        _defs.Clear();
        _defs.AddRange(list);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool TryAdd(MathsChannelDefinition definition, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(definition.Id))
        {
            error = "Id cannot be empty.";
            return false;
        }

        if (!IsValidId(definition.Id))
        {
            error = "Id allows letters/digits/_ only, and must not start with a digit.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(definition.Expression))
        {
            error = "Expression cannot be empty.";
            return false;
        }

        if (_defs.Any(d => string.Equals(d.Id, definition.Id, StringComparison.OrdinalIgnoreCase)))
        {
            error = $"Channel Id already exists: {definition.Id}";
            return false;
        }

        if (!MathsExpression.TryEvaluate(definition.Expression, _ => 0, out _, out var evalError))
        {
            error = evalError ?? "Invalid expression.";
            return false;
        }

        _defs.Add(definition);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Remove(string id)
    {
        var n = _defs.RemoveAll(d =>
            string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        if (n > 0)
            Changed?.Invoke(this, EventArgs.Empty);
        return n > 0;
    }

    public static bool IsValidId(string id)
    {
        if (string.IsNullOrEmpty(id))
            return false;
        if (!(char.IsLetter(id[0]) || id[0] == '_'))
            return false;
        for (var i = 1; i < id.Length; i++)
        {
            if (!(char.IsLetterOrDigit(id[i]) || id[i] == '_'))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Build a stable Id from a display name (VBTS names may contain '.' etc.).
    /// </summary>
    public static string SanitizeId(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Maths";

        var sb = new StringBuilder();
        foreach (var c in name.Trim())
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '_')
                sb.Append('_');
        }

        var s = sb.ToString().Trim('_');
        if (string.IsNullOrEmpty(s))
            s = "Maths";
        if (char.IsDigit(s[0]))
            s = "_" + s;
        return s;
    }

    /// <summary>Unique Id among <paramref name="existing"/> (case-insensitive).</summary>
    public static string AllocateUniqueId(string preferred, IEnumerable<string> existing)
    {
        var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var baseId = SanitizeId(preferred);
        if (!set.Contains(baseId))
            return baseId;

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{baseId}_{i}";
            if (!set.Contains(candidate))
                return candidate;
        }

        return $"{baseId}_{Guid.NewGuid():N}"[..24];
    }
}
