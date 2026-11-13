using Chassis_Master_Test_Suite.Core;

namespace Chassis_Master_Test_Suite.Session;

/// <summary>
/// Selects the stable sample list used by plots, cursors and analysis.
/// Offline files retain their complete list and original indices; only live
/// acquisition uses the bounded history buffer. Call from the UI thread that
/// owns <see cref="OfflineFileSet"/>.
/// </summary>
public static class ActiveSampleHistory
{
    public static IReadOnlyList<VehicleSample> GetSnapshot(
        bool offlineMode,
        OfflineFileSet offlineFiles,
        SampleHistoryBuffer liveHistory)
    {
        if (offlineMode)
            return offlineFiles.Primary?.Samples ?? Array.Empty<VehicleSample>();

        return liveHistory.Snapshot();
    }
}
