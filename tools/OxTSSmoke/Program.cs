using Chassis_Master_Test_Suite.Recorder.OxTS;

var xcomPath = args.Length > 0 ? args[0] : @"D:\CMTS\cmts-xcom\samples\oxts-xcom\mobile-015.xcom";
var ncomPath = args.Length > 1 ? args[1] : @"D:\CMTS\cmts-xcom\samples\oxts-xcom\mobile-015.ncom";

var xcom = XcomContainerReader.ReadFile(xcomPath);
Console.WriteLine($"pages={xcom.PageCount} streams={xcom.Streams.Count}");
foreach (var s in xcom.Streams)
    Console.WriteLine($"  s{s.Serial} {s.Kind} dataPkts={s.DataPackets.Count()} aux={s.AuxHeaders.Count()} label={s.Label}");

var ncomStream = xcom.Streams.Single(s => s.Serial == 2);
var demux = new List<byte>();
foreach (var pkt in ncomStream.DataPackets)
    demux.AddRange(pkt.Payload);
var standalone = File.ReadAllBytes(ncomPath);
Console.WriteLine($"demux==ncom? {demux.Count == standalone.Length && demux.SequenceEqual(standalone)} ({demux.Count}/{standalone.Length})");

var imported = XcomImporter.ImportFile(xcomPath);
Console.WriteLine(imported.Summary);
foreach (var t in imported.Tracks)
    Console.WriteLine($"  track {t.DisplayName} kind={t.Kind} samples={t.Samples.Count} cols={t.ColumnNames.Count}");

var hunter = imported.Tracks.Single(t => t.StreamSerial == 2 && t.Kind == XcomStreamKind.Ncom);
var s0 = hunter.Samples[0];
Console.WriteLine($"hunter0 lat={s0.Latitude:F6} lon={s0.Longitude:F6} spd={s0.SpeedKph:F2} hdg={s0.Heading:F2}");
if (Math.Abs(s0.Latitude - 42.61939) > 0.001) throw new Exception("lat mismatch");
if (hunter.Samples.Count < 1000) throw new Exception("too few hunter samples");
if (imported.Tracks.Count(t => t.Kind == XcomStreamKind.Ncom) != 2) throw new Exception("expected 2 ncom");
if (imported.Tracks.Count(t => t.Kind == XcomStreamKind.Rcom) != 1) throw new Exception("expected 1 rcom");
Console.WriteLine("SMOKE OK");
