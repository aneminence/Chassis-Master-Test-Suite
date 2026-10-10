using System.Collections.Generic;

namespace Chassis_Master_Test_Suite.Core;

/// <summary>
/// 曲线绘制用的可见窗口 + 降采样。
///
/// 目标：历史很长时避免每次 10Hz 全量 <c>new double[Count]</c>，
/// 同时用 min-max 桶保留尖峰，避免简单 stride 抹掉峰值。
/// </summary>
public static class PlotDownsampler
{
    /// <summary>
    /// 单条曲线重建时允许的最大点数（降采样后）。
    /// 取 12000：够密、远低于百万级历史，也低于常见屏幕像素宽的若干倍。
    /// </summary>
    public const int MaxPlotPoints = 12_000;

    /// <summary>
    /// 在样本上按 X 找可见下标区间 <c>[start, end)</c>。
    /// <paramref name="useFullRange"/> 为 true（Auto X）时直接返回全长。
    /// X 近似单调时可走二分；否则线性扫一遍落在 [left,right] 的下标包络。
    /// </summary>
    public static (int Start, int EndExclusive) FindVisibleIndexRange(
        int count,
        Func<int, double> getX,
        double left,
        double right,
        bool useFullRange)
    {
        if (count <= 0)
            return (0, 0);

        if (useFullRange ||
            double.IsNaN(left) ||
            double.IsNaN(right) ||
            double.IsInfinity(left) ||
            double.IsInfinity(right) ||
            right <= left)
        {
            return (0, count);
        }

        // 轻微外扩，避免平移边缘缺一两个点
        var pad = (right - left) * 0.02;
        if (pad <= 0)
            pad = 1e-9;
        var lo = left - pad;
        var hi = right + pad;

        if (IsMonotonicNonDecreasing(count, getX))
        {
            var start = LowerBound(count, getX, lo);
            var end = UpperBound(count, getX, hi);
            if (end <= start)
                return (0, count);
            return (start, end);
        }

        var first = -1;
        var last = -1;
        for (var i = 0; i < count; i++)
        {
            var x = getX(i);
            if (x < lo || x > hi)
                continue;
            if (first < 0)
                first = i;
            last = i;
        }

        if (first < 0)
            return (0, count);

        return (first, last + 1);
    }

    /// <summary>
    /// 对可见区间做 min-max 降采样，返回按时间顺序的原始下标。
    /// 点数 ≤ <see cref="MaxPlotPoints"/> 时原样拷贝下标。
    /// </summary>
    public static int[] BuildDownsampleIndices(
        int startInclusive,
        int endExclusive,
        Func<int, double> getY,
        int maxPoints = MaxPlotPoints)
    {
        if (maxPoints < 2)
            maxPoints = 2;

        var count = endExclusive - startInclusive;
        if (count <= 0)
            return Array.Empty<int>();

        if (count <= maxPoints)
        {
            var all = new int[count];
            for (var i = 0; i < count; i++)
                all[i] = startInclusive + i;
            return all;
        }

        // 每桶最多吐 2 点（min + max）→ 桶数 ≈ maxPoints/2
        var bucketCount = Math.Max(1, maxPoints / 2);
        var indices = new List<int>(Math.Min(maxPoints + 2, count));

        indices.Add(startInclusive);

        for (var b = 0; b < bucketCount; b++)
        {
            var bStart = startInclusive + (int)((long)b * count / bucketCount);
            var bEnd = startInclusive + (int)((long)(b + 1) * count / bucketCount);
            if (bEnd <= bStart)
                continue;

            // 首桶已含全局首点，从下一个样本开始找极值，避免重复
            var scanStart = bStart;
            if (b == 0 && bStart == startInclusive && bEnd > bStart + 1)
                scanStart = bStart + 1;

            if (scanStart >= bEnd)
                continue;

            var iMin = scanStart;
            var iMax = scanStart;
            var vMin = getY(scanStart);
            var vMax = vMin;

            for (var i = scanStart + 1; i < bEnd; i++)
            {
                var v = getY(i);
                if (v < vMin)
                {
                    vMin = v;
                    iMin = i;
                }

                if (v > vMax)
                {
                    vMax = v;
                    iMax = i;
                }
            }

            AppendIndexOrdered(indices, iMin, iMax);
        }

        var last = endExclusive - 1;
        if (indices[^1] != last)
            indices.Add(last);

        // 极端情况下 List 可能略超 maxPoints（首尾 + 桶），裁到上限并保证含尾
        if (indices.Count > maxPoints)
        {
            var trimmed = new int[maxPoints];
            trimmed[0] = indices[0];
            var lastIdx = indices[^1];
            var inner = maxPoints - 2;
            for (var i = 0; i < inner; i++)
            {
                var src = 1 + (int)((long)i * (indices.Count - 2) / Math.Max(1, inner));
                trimmed[i + 1] = indices[src];
            }

            trimmed[maxPoints - 1] = lastIdx;
            return trimmed;
        }

        return indices.ToArray();
    }

    /// <summary>
    /// 按共享下标抽出 X / Y 数组（与光标用的 LastXs / LastSamples 对齐）。
    /// </summary>
    public static (double[] Xs, double[] Ys) ExtractSeries(
        IReadOnlyList<VehicleSample> samples,
        int[] indices,
        Func<VehicleSample, double> selectX,
        Func<VehicleSample, double> selectY)
    {
        var n = indices.Length;
        var xs = new double[n];
        var ys = new double[n];
        for (var i = 0; i < n; i++)
        {
            var sample = samples[indices[i]];
            xs[i] = selectX(sample);
            ys[i] = selectY(sample);
        }

        return (xs, ys);
    }

    public static double[] ExtractXs(
        IReadOnlyList<VehicleSample> samples,
        int[] indices,
        Func<VehicleSample, double> selectX)
    {
        var xs = new double[indices.Length];
        for (var i = 0; i < indices.Length; i++)
            xs[i] = selectX(samples[indices[i]]);
        return xs;
    }

    public static double[] ExtractYs(
        IReadOnlyList<VehicleSample> samples,
        int[] indices,
        Func<VehicleSample, double> selectY)
    {
        var ys = new double[indices.Length];
        for (var i = 0; i < indices.Length; i++)
            ys[i] = selectY(samples[indices[i]]);
        return ys;
    }

    public static VehicleSample[] ExtractSamples(
        IReadOnlyList<VehicleSample> samples,
        int[] indices)
    {
        var result = new VehicleSample[indices.Length];
        for (var i = 0; i < indices.Length; i++)
            result[i] = samples[indices[i]];
        return result;
    }

    private static void AppendIndexOrdered(List<int> indices, int a, int b)
    {
        if (a == b)
        {
            if (indices[^1] != a)
                indices.Add(a);
            return;
        }

        if (a < b)
        {
            if (indices[^1] != a)
                indices.Add(a);
            if (indices[^1] != b)
                indices.Add(b);
        }
        else
        {
            if (indices[^1] != b)
                indices.Add(b);
            if (indices[^1] != a)
                indices.Add(a);
        }
    }

    private static bool IsMonotonicNonDecreasing(int count, Func<int, double> getX)
    {
        if (count <= 2)
            return true;

        // 抽样检查，避免 O(n) 太重；失败则退回线性可见窗
        var step = Math.Max(1, count / 64);
        var prev = getX(0);
        for (var i = step; i < count; i += step)
        {
            var x = getX(i);
            if (x < prev)
                return false;
            prev = x;
        }

        return getX(count - 1) >= prev;
    }

    private static int LowerBound(int count, Func<int, double> getX, double value)
    {
        var lo = 0;
        var hi = count;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (getX(mid) < value)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }

    private static int UpperBound(int count, Func<int, double> getX, double value)
    {
        var lo = 0;
        var hi = count;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (getX(mid) <= value)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }
}
