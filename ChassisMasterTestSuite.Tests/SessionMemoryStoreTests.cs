using Chassis_Master_Test_Suite.Analysis;
using Chassis_Master_Test_Suite.Core;

namespace ChassisMasterTestSuite.Tests;

public class SessionMemoryStoreTests
{
    [Fact]
    public void GateDto_RoundTrips_IdentityAndPose()
    {
        var gate = new GateDefinition
        {
            Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Name = "Gate Speed",
            Latitude = 31.2,
            Longitude = 121.5,
            WidthMeters = 10,
            HeadingDeg = 90,
            ColorHex = "#4A9FD8"
        };

        var dto = SessionMemoryStore.FromGate(gate);
        var back = SessionMemoryStore.ToGate(dto);

        Assert.Equal(gate.Id, back.Id);
        Assert.Equal(gate.Name, back.Name);
        Assert.Equal(gate.Latitude, back.Latitude);
        Assert.Equal(gate.Longitude, back.Longitude);
        Assert.Equal(gate.WidthMeters, back.WidthMeters);
        Assert.Equal(gate.HeadingDeg, back.HeadingDeg);
        Assert.Equal(gate.ColorHex, back.ColorHex);
    }

    [Fact]
    public void Snapshot_Serializes_AndDeserializes()
    {
        var path = Path.Combine(Path.GetTempPath(), "cmts-session-memory-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var snap = new SessionMemorySnapshot
            {
                IsOfflineMode = true,
                VboFiles = { @"D:\CMTS\a.vbo", @"D:\CMTS\b.vbo" },
                ActiveVboPath = @"D:\CMTS\b.vbo",
                LastVbtsPath = @"D:\CMTS\kx21.vbts",
                Gates =
                {
                    new SessionGateDto
                    {
                        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        Name = "Start",
                        Latitude = 1, Longitude = 2, WidthMeters = 10, ColorHex = "#3FBF6F"
                    }
                },
                AnalysisStartGateId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TestResults = new TestResultsSettingsDto
                {
                    Type = "Gate",
                    StartThreshold = "",
                    EndThreshold = "",
                    PassConditions =
                    {
                        new PassConditionSettingsDto
                        {
                            ChannelId = "velocity",
                            MinText = "78",
                            MaxText = "83",
                            AtGateId = Guid.Parse("11111111-1111-1111-1111-111111111111")
                        }
                    }
                },
                MathsChannels =
                {
                    new SessionMathsDto
                    {
                        Id = "Speed",
                        Expression = "velocity * 1.16",
                        DisplayName = "Speed",
                        Unit = "km/h"
                    }
                },
                Curves = new CurvesSettingsDto
                {
                    XAxisChannelId = "axis_time",
                    XAutoScale = true,
                    SinglePlotFillsViewport = false,
                    Plots =
                    {
                        new PlotSettingsDto
                        {
                            Name = "Plot 1",
                            AutoScaleY = true,
                            HeightPx = 320,
                            ChannelIds = { "velocity", "long_acc" }
                        },
                        new PlotSettingsDto
                        {
                            Name = "Plot 2",
                            AutoScaleY = false,
                            HeightPx = 200,
                            ChannelIds = { "lat_acc" }
                        }
                    }
                }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(snap, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
            File.WriteAllText(path, json);

            var loaded = System.Text.Json.JsonSerializer.Deserialize<SessionMemorySnapshot>(
                File.ReadAllText(path),
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });

            Assert.NotNull(loaded);
            Assert.True(loaded!.IsOfflineMode);
            Assert.Equal(2, loaded.VboFiles.Count);
            Assert.Equal(@"D:\CMTS\b.vbo", loaded.ActiveVboPath);
            Assert.Equal(@"D:\CMTS\kx21.vbts", loaded.LastVbtsPath);
            Assert.Single(loaded.Gates);
            Assert.Equal("Start", loaded.Gates[0].Name);
            Assert.Equal("Gate", loaded.TestResults!.Type);
            Assert.Single(loaded.TestResults.PassConditions);
            Assert.Equal("78", loaded.TestResults.PassConditions[0].MinText);
            Assert.Single(loaded.MathsChannels);
            Assert.Equal("velocity * 1.16", loaded.MathsChannels[0].Expression);
            Assert.NotNull(loaded.Curves);
            Assert.Equal(2, loaded.Curves!.Plots.Count);
            Assert.Equal(320, loaded.Curves.Plots[0].HeightPx);
            Assert.Equal(2, loaded.Curves.Plots[0].ChannelIds.Count);
            Assert.Equal("lat_acc", loaded.Curves.Plots[1].ChannelIds[0]);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }


    [Fact]
    public void MathsChannels_Persist_Via_SaveLoad_And_Store()
    {
        var path = Path.Combine(Path.GetTempPath(), "cmts-maths-session-" + Guid.NewGuid().ToString("N") + ".json");
        var prev = SessionMemoryStore.StoragePathOverride;
        try
        {
            SessionMemoryStore.StoragePathOverride = path;

            var snap = new SessionMemorySnapshot
            {
                IsOfflineMode = true,
                MathsChannels =
                {
                    new SessionMathsDto
                    {
                        Id = "Speed",
                        Expression = "velocity * 1.16",
                        DisplayName = "Speed",
                        Unit = "km/h"
                    },
                    new SessionMathsDto
                    {
                        Id = "AyAbs",
                        Expression = "lat_accel * -1",
                        DisplayName = "AyAbs",
                        Unit = "g"
                    }
                }
            };
            SessionMemoryStore.Save(snap);

            var loaded = SessionMemoryStore.Load();
            Assert.NotNull(loaded);
            Assert.Equal(2, loaded!.MathsChannels.Count);
            Assert.Equal("velocity * 1.16", loaded.MathsChannels[0].Expression);
            Assert.Equal("AyAbs", loaded.MathsChannels[1].Id);

            var store = new MathsChannelStore();
            var defs = loaded.MathsChannels.Select(SessionMemoryStore.ToMaths).ToList();
            Assert.True(store.TryReplaceAll(defs, out var error), error);
            Assert.Equal(2, store.Definitions.Count);
            Assert.Equal("Speed", store.Definitions[0].DisplayName);
            Assert.Equal("lat_accel * -1", store.Definitions[1].Expression);

            // Round-trip store -> DTO -> save -> load again
            var snap2 = new SessionMemorySnapshot();
            foreach (var d in store.Definitions)
                snap2.MathsChannels.Add(SessionMemoryStore.FromMaths(d));
            SessionMemoryStore.Save(snap2);

            var loaded2 = SessionMemoryStore.Load();
            Assert.NotNull(loaded2);
            Assert.Equal(2, loaded2!.MathsChannels.Count);
            Assert.Equal("velocity * 1.16", loaded2.MathsChannels[0].Expression);
            Assert.Equal("km/h", loaded2.MathsChannels[0].Unit);
        }
        finally
        {
            SessionMemoryStore.StoragePathOverride = prev;
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void GateStore_ReplaceAll_PreservesIdsAndRoles()
    {
        var store = new GateStore();
        var a = new GateDefinition { Id = Guid.NewGuid(), Name = "A", Latitude = 1, Longitude = 2, ColorHex = "#3FBF6F" };
        var b = new GateDefinition { Id = Guid.NewGuid(), Name = "B", Latitude = 3, Longitude = 4, ColorHex = "#4A9FD8" };
        store.ReplaceAll(new[] { a, b }, selectedId: b.Id, analysisStartId: a.Id, analysisEndId: b.Id);

        Assert.Equal(2, store.Gates.Count);
        Assert.Equal(b.Id, store.SelectedId);
        Assert.Equal(a.Id, store.AnalysisStartId);
        Assert.Equal(b.Id, store.AnalysisEndId);
        Assert.Equal("A", store.StartGate!.Name);
        Assert.Equal("B", store.EndGate!.Name);
    }
}
