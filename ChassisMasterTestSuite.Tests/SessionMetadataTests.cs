using Chassis_Master_Test_Suite.Session;

namespace ChassisMasterTestSuite.Tests;

public class SessionMetadataTests
{
    [Fact]
    public void RoundTrip_Lines()
    {
        var m = new SessionMetadata
        {
            DriverName = "Alice",
            VehicleModel = "CMTS-1",
            TestTrack = "Straight",
            Comments = "P1 test"
        };
        var parsed = SessionMetadata.FromVboLines(m.ToVboLines());
        Assert.Equal("Alice", parsed.DriverName);
        Assert.Equal("CMTS-1", parsed.VehicleModel);
        Assert.Equal("Straight", parsed.TestTrack);
        Assert.Equal("P1 test", parsed.Comments);
    }
}
