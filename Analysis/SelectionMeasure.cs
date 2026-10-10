using System.Globalization;
using System.Text;
using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Analysis;

/// <summary>
/// 曲线选区统计：min / max / avg（按样本，不按插值）。
/// </summary>
public static class SelectionMeasure
{
    public sealed class ChannelStats
    {
        public required string ChannelId { get; init; }
        public int Count { get; init; }
        public double Min { get; init; }
        public double Max { get; init; }
        public double Avg { get; init; }
        public int StartIndex { get; init; }
        public int EndIndex { get; init; }
    }

    public sealed class Result
    {
        public double X1 { get; init; }
        public double X2 { get; init; }
        public int StartIndex { get; init; }
        public int EndIndex { get; init; }
        public IReadOnlyList<ChannelStats> Channels { get; init; } = Array.Empty<ChannelStats>();
    }

    public static Result? Compute(
        IReadOnlyList<VehicleSample> samples,
        Func<VehicleSample, double> getAxisX,
        double x1,
        double x2,
        IReadOnlyList<string> channelIds)
    {
        if (samples.Count == 0 || channelIds.Count == 0)
            return null;

        var left = Math.Min(x1, x2);
        var right = Math.Max(x1, x2);
        var indices = new List<int>();
        for (var i = 0; i < samples.Count; i++)
        {
            var x = getAxisX(samples[i]);
            if (x >= left && x <= right)
                indices.Add(i);
        }

        if (indices.Count == 0)
            return null;

        var start = indices[0];
        var end = indices[^1];
        var channelStats = new List<ChannelStats>();
        foreach (var id in channelIds)
        {
            double min = double.PositiveInfinity;
            double max = double.NegativeInfinity;
            double sum = 0;
            var n = 0;
            foreach (var i in indices)
            {
                var v = samples[i].GetChannel(id);
                if (double.IsNaN(v) || double.IsInfinity(v))
                    continue;
                min = Math.Min(min, v);
                max = Math.Max(max, v);
                sum += v;
                n++;
            }

            if (n == 0)
                continue;

            channelStats.Add(new ChannelStats
            {
                ChannelId = id,
                Count = n,
                Min = min,
                Max = max,
                Avg = sum / n,
                StartIndex = start,
                EndIndex = end
            });
        }

        return new Result
        {
            X1 = left,
            X2 = right,
            StartIndex = start,
            EndIndex = end,
            Channels = channelStats
        };
    }

    public static string Format(Result result, Func<string, string>? displayName = null)
    {
        var sb = new StringBuilder();
        sb.Append("Measure [").Append(result.StartIndex).Append("..").Append(result.EndIndex).Append(']');
        foreach (var c in result.Channels)
        {
            var name = displayName?.Invoke(c.ChannelId) ?? c.ChannelId;
            sb.AppendLine();
            sb.Append(name)
                .Append("  min=")
                .Append(F(c.Min))
                .Append("  max=")
                .Append(F(c.Max))
                .Append("  avg=")
                .Append(F(c.Avg))
                .Append("  n=")
                .Append(c.Count);
        }

        return sb.ToString();
    }

    private static string F(double v) =>
        v.ToString("0.###", CultureInfo.InvariantCulture);
}
