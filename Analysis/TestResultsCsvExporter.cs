using System.IO;
using System.Globalization;
using System.Text;

namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>
/// Export test runs to CSV (UTF-8 BOM for Excel).
/// </summary>
public static class TestResultsCsvExporter
{
    public static string BuildCsv(IReadOnlyList<TestRunResult> runs, TestDefinition? definition = null)
    {
        var passHeaders = CollectPassHeaders(runs);
        var sb = new StringBuilder();
        sb.Append(
            "Run,Source,StartTimestampMs,EndTimestampMs,Duration_s,StartSpeed_kph,EndSpeed_kph,DeltaV_kph,Distance_m,DistanceMethod,Pass,FailReason");
        foreach (var h in passHeaders)
        {
            sb.Append(',').Append(CsvEscape(h));
            sb.Append(',').Append(CsvEscape(h + "_InRange"));
        }
        sb.AppendLine();

        foreach (var r in runs)
        {
            sb.Append(r.RunNumber).Append(',');
            sb.Append(CsvEscape(r.SourceFile)).Append(',');
            sb.Append(r.StartTimestampMs).Append(',');
            sb.Append(r.EndTimestampMs).Append(',');
            sb.Append(F(r.DurationSeconds)).Append(',');
            sb.Append(F(r.StartSpeedKph)).Append(',');
            sb.Append(F(r.EndSpeedKph)).Append(',');
            sb.Append(F(r.DeltaSpeedKph)).Append(',');
            sb.Append(F(r.DistanceMeters)).Append(',');
            sb.Append(CsvEscape(r.DistanceMethod)).Append(',');
            sb.Append(r.Pass ? "Pass" : "Fail").Append(',');
            sb.Append(CsvEscape(r.FailReason));

            var byHeader = r.PassMeasurements
                .GroupBy(m => m.Header, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            foreach (var h in passHeaders)
            {
                sb.Append(',');
                if (byHeader.TryGetValue(h, out var m))
                {
                    sb.Append(m.Value is double v ? F(v) : "");
                    sb.Append(',').Append(m.InRange ? "1" : "0");
                }
                else
                {
                    sb.Append(',').Append('0');
                }
            }

            sb.AppendLine();
        }

        if (definition is not null)
        {
            sb.AppendLine();
            sb.Append("# TestType=").Append(definition.Type)
                .Append(" Start=").Append(definition.Start.ChannelId)
                .Append('@').Append(F(definition.Start.Threshold))
                .Append(' ').Append(definition.Start.Direction)
                .Append(" End=").Append(definition.End.ChannelId)
                .Append('@').Append(F(definition.End.Threshold))
                .Append(' ').Append(definition.End.Direction)
                .AppendLine();
        }

        return sb.ToString();
    }

    public static void WriteFile(string path, IReadOnlyList<TestRunResult> runs, TestDefinition? definition = null)
    {
        var csv = BuildCsv(runs, definition);
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>Stable union of Pass measurement headers across all runs (first-seen order).</summary>
    public static IReadOnlyList<string> CollectPassHeaders(IReadOnlyList<TestRunResult> runs)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in runs)
        {
            foreach (var m in r.PassMeasurements)
            {
                if (seen.Add(m.Header))
                    list.Add(m.Header);
            }
        }
        return list;
    }

    private static string F(double v) =>
        v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";

        return value;
    }
}
