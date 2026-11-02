namespace Chassis_Master_Test_Suite.Communication.GSpot;

/// <summary>
/// Pure vehicle demux / filter for GSpot rooms that may push multiple cars.
/// </summary>
public static class GSpotVehicleFilter
{
    /// <summary>
    /// Decide whether a parsed frame should be published.
    /// When filters are set, only matching frames pass.
    /// When no filters and <paramref name="acceptAllVehicles"/> is true, all frames pass.
    /// When no filters and acceptAll is false, lock onto the first vehicle (legacy).
    /// </summary>
    public static bool ShouldAccept(
        string? filterGUserId,
        string? filterCNum,
        bool acceptAllVehicles,
        ref string? lockedGUserId,
        ref string? lockedCNum,
        string? gUserId,
        string? cNum)
    {
        var hasFilter =
            !string.IsNullOrWhiteSpace(filterGUserId) ||
            !string.IsNullOrWhiteSpace(filterCNum);

        if (hasFilter)
        {
            if (!string.IsNullOrWhiteSpace(filterGUserId) &&
                !string.Equals(filterGUserId, gUserId, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(filterCNum) &&
                !string.Equals(filterCNum, cNum, StringComparison.Ordinal))
            {
                return false;
            }

            lockedGUserId ??= gUserId;
            lockedCNum ??= cNum;
            return true;
        }

        if (acceptAllVehicles)
            return true;

        // Legacy: lock first vehicle seen.
        if (lockedGUserId is null && lockedCNum is null)
        {
            lockedGUserId = gUserId;
            lockedCNum = cNum;
            return true;
        }

        if (lockedGUserId is not null &&
            !string.Equals(lockedGUserId, gUserId, StringComparison.Ordinal))
        {
            return false;
        }

        if (lockedCNum is not null &&
            !string.Equals(lockedCNum, cNum, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    /// <summary>Stable map key for a sample / frame.</summary>
    public static string ResolveKey(string? vehicleId, string? vehicleLabel)
    {
        if (!string.IsNullOrWhiteSpace(vehicleId))
            return vehicleId.Trim();
        if (!string.IsNullOrWhiteSpace(vehicleLabel))
            return vehicleLabel.Trim();
        return "default";
    }
}
