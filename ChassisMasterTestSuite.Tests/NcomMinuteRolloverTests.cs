using System.Buffers.Binary;
using Chassis_Master_Test_Suite.Recorder.OxTS;

namespace ChassisMasterTestSuite.Tests;

public class NcomMinuteRolloverTests
{
    private const uint Minute = 24_000_000;

    [Fact]
    public void RolloverKeepsInferredMinuteUntilNextStatusUpdate()
    {
        var samples = new[]
        {
            Packet(59_980, Minute), Packet(59_990), Packet(0),
            Packet(10), Packet(20), Packet(30, Minute + 1)
        };

        Assert.Equal(Enumerable.Range(0, 6).Select(i => Stamp(Minute, 59_980) + i * 10),
            NcomTime.AssignUnixTimestamps(samples));
    }

    [Fact]
    public void StatusUpdateAtRolloverDoesNotIncrementMinuteTwice()
    {
        var samples = new[] { Packet(59_990, Minute), Packet(0, Minute + 1), Packet(10) };

        Assert.Equal(new[] { Stamp(Minute, 59_990), Stamp(Minute + 1, 0), Stamp(Minute + 1, 10) },
            NcomTime.AssignUnixTimestamps(samples));
    }

    [Fact]
    public void LeadingPacketsAreBackfilledAcrossMultipleMinuteBoundaries()
    {
        var samples = new[]
        {
            Packet(59_990), Packet(0), Packet(59_990), Packet(0), Packet(10, Minute + 2)
        };

        Assert.Equal(new[]
        {
            Stamp(Minute, 59_990), Stamp(Minute + 1, 0), Stamp(Minute + 1, 59_990),
            Stamp(Minute + 2, 0), Stamp(Minute + 2, 10)
        }, NcomTime.AssignUnixTimestamps(samples));
    }

    [Fact]
    public void InvalidTimeDoesNotLoseMinuteOrPreviousValidMillisecond()
    {
        var samples = new[]
        {
            Packet(59_990, Minute), Packet(ushort.MaxValue), Packet(0),
            Packet(ushort.MaxValue), Packet(10)
        };

        Assert.Equal(new[]
        {
            Stamp(Minute, 59_990), Stamp(Minute, 59_990), Stamp(Minute + 1, 0),
            Stamp(Minute + 1, 0), Stamp(Minute + 1, 10)
        }, NcomTime.AssignUnixTimestamps(samples));
    }

    [Fact]
    public void LeadingInvalidTimeDoesNotHideMinuteBoundary()
    {
        var samples = new[] { Packet(59_990), Packet(ushort.MaxValue), Packet(0, Minute + 1) };

        Assert.Equal(new[] { Stamp(Minute, 59_990), Stamp(Minute, 59_990), Stamp(Minute + 1, 0) },
            NcomTime.AssignUnixTimestamps(samples));
    }

    [Fact]
    public void SmallOutOfOrderTimesArePreservedRatherThanClampedOrSorted()
    {
        var samples = new[] { Packet(100, Minute), Packet(90), Packet(110) };

        Assert.Equal(new[] { Stamp(Minute, 100), Stamp(Minute, 90), Stamp(Minute, 110) },
            NcomTime.AssignUnixTimestamps(samples));
    }

    [Fact]
    public void NoMinuteAnchorKeepsExistingRelativeFallback()
    {
        var samples = new[] { Packet(59_990), Packet(0), Packet(10) };

        Assert.Equal(new long[] { 0, 10, 20 }, NcomTime.AssignUnixTimestamps(samples));
    }

    [Fact]
    public void XcomPrefixesRemainAuthoritativeForKnownAndRelativeTime()
    {
        const long knownNs = 1_472_383_616_400_000_000L;
        var samples = new[]
        {
            Packet(59_990, Minute, knownNs), Packet(0, Minute + 1, knownNs + 10_000_000),
            Packet(10, prefixNs: 123_456_789)
        };

        Assert.Equal(new[] { NcomTime.GpsNsToUnixMs(knownNs), NcomTime.GpsNsToUnixMs(knownNs) + 10, 123 },
            NcomTime.AssignUnixTimestamps(samples));
    }

    [Fact]
    public void PrefixedRolloverAdvancesFallbackForFollowingUnprefixedPackets()
    {
        var rolloverNs = (Minute + 1L) * 60_000_000_000;
        var samples = new[]
        {
            Packet(59_990, Minute), Packet(0, prefixNs: rolloverNs), Packet(10)
        };

        Assert.Equal(new[] { Stamp(Minute, 59_990), Stamp(Minute + 1, 0), Stamp(Minute + 1, 10) },
            NcomTime.AssignUnixTimestamps(samples));
    }

    private static NcomMeasurement Packet(ushort milliseconds, uint? minute = null, long? prefixNs = null)
    {
        var status = new byte[8];
        if (minute.HasValue) BinaryPrimitives.WriteUInt32LittleEndian(status, minute.Value);
        return new NcomMeasurement
        {
            TimeMsIntoMinute = milliseconds,
            NavigationStatus = 4,
            StatusChannel = minute.HasValue ? (byte)0 : (byte)4,
            StatusData = status,
            TimePrefixNs = prefixNs
        };
    }

    private static long Stamp(uint minute, int milliseconds) =>
        NcomTime.GpsMinutesAndMsToUnixMs(minute, milliseconds);
}
