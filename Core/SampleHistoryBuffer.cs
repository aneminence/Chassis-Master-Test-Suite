using System.Collections.Generic;

namespace Chassis_Master_Test_Suite.Core;

/// <summary>
/// 线程安全的有界环形历史缓冲。
///
/// 消费线程（DataBus）与 UI 定时器并发读写时，
/// 禁止再直接暴露可变 List；UI 只通过 <see cref="Snapshot"/> 拿稳定副本。
/// 满容量时 O(1) 覆盖最旧样本，不再使用 <c>RemoveAt(0)</c>。
/// </summary>
public sealed class SampleHistoryBuffer
{
    private readonly object _gate = new();
    private readonly VehicleSample?[] _buffer;

    /// <summary>下一次写入的下标。</summary>
    private int _head;

    /// <summary>当前有效样本数（0..Capacity）。</summary>
    private int _count;

    public SampleHistoryBuffer(int capacity = 1_000_000)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                capacity,
                "Capacity must be at least 1.");
        }

        _buffer = new VehicleSample?[capacity];
    }

    public int Capacity => _buffer.Length;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    public void Add(VehicleSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        lock (_gate)
        {
            AddUnlocked(sample);
        }
    }

    public void AddRange(IEnumerable<VehicleSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        lock (_gate)
        {
            foreach (var sample in samples)
            {
                if (sample is null)
                    continue;

                AddUnlocked(sample);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _head = 0;
            _count = 0;
        }
    }

    /// <summary>
    /// 返回从最旧到最新的稳定副本，供 UI / Track Map / 曲线使用。
    /// </summary>
    public IReadOnlyList<VehicleSample> Snapshot()
    {
        lock (_gate)
        {
            if (_count == 0)
                return Array.Empty<VehicleSample>();

            var result = new VehicleSample[_count];
            var start = _count < _buffer.Length ? 0 : _head;

            for (var i = 0; i < _count; i++)
            {
                var index = (start + i) % _buffer.Length;
                result[i] = _buffer[index]!;
            }

            return result;
        }
    }

    private void AddUnlocked(VehicleSample sample)
    {
        _buffer[_head] = sample;
        _head = (_head + 1) % _buffer.Length;

        if (_count < _buffer.Length)
        {
            _count++;
        }
        // else: 已满，_head 已指向被覆盖后的最旧位置，count 保持 Capacity。
    }
}
