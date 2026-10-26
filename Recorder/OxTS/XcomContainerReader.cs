using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Chassis_Master_Test_Suite.Recorder.OxTS;

/// <summary>
/// Demultiplexes an OxTS XCOM (Ogg-like) container into logical streams.
/// Does not decode NCOM/RCOM payloads — see <see cref="NcomDecoder"/> / <see cref="RcomDecoder"/>.
/// Spec: OxTS XCOM Manual (capture pattern OggS, granule ns, BOS initial header, headers=N aux packets).
/// </summary>
public sealed class XcomContainerReader
{
    /// <summary>GPS-known granule threshold from XCOM Manual (ns).</summary>
    public const long GpsKnownGranuleThresholdNs = 31_190_400_000_000_000L;

    /// <summary>Per-packet wall time prefix length observed in NAVsuite-written XCOM data packets.</summary>
    public const int DataPacketTimePrefixBytes = 8;

    public static XcomFile ReadFile(string path, CancellationToken cancellationToken = default)
    {
        using var fs = File.OpenRead(path);
        return Read(fs, Path.GetFileName(path), cancellationToken);
    }

    public static XcomFile Read(Stream stream, string? displayName = null, CancellationToken cancellationToken = default)
    {
        // XCOM files in practice are a few–tens of MB; load once for random access demux.
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var data = ms.ToArray();
        return Parse(data, displayName ?? "xcom", cancellationToken);
    }

    public static XcomFile Parse(ReadOnlySpan<byte> data, string displayName, CancellationToken cancellationToken = default)
    {
        var streams = new Dictionary<uint, StreamBuilder>();
        var offset = 0;
        var pages = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var i = IndexOfCapture(data, offset);
            if (i < 0 || i + 27 > data.Length)
                break;

            var version = data[i + 4];
            var headerType = data[i + 5];
            var granule = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(i + 6, 8));
            var serial = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i + 14, 4));
            var pageSeq = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i + 18, 4));
            var nseg = data[i + 26];
            if (i + 27 + nseg > data.Length)
                break;

            var segs = data.Slice(i + 27, nseg);
            var bodyLen = 0;
            for (var s = 0; s < nseg; s++)
                bodyLen += segs[s];

            var bodyOff = i + 27 + nseg;
            if (bodyOff + bodyLen > data.Length)
                break;

            if (!streams.TryGetValue(serial, out var builder))
            {
                builder = new StreamBuilder(serial);
                streams[serial] = builder;
            }

            builder.NotePage(pageSeq, granule, headerType);

            var continued = (headerType & 0x01) != 0;
            var bos = (headerType & 0x02) != 0;
            var eos = (headerType & 0x04) != 0;

            var body = data.Slice(bodyOff, bodyLen);
            if (bos)
            {
                // BOS payload is the initial header packet (usually one segment).
                builder.SetBos(Encoding.ASCII.GetString(body));
            }
            else
            {
                var pos = 0;
                for (var s = 0; s < nseg; s++)
                {
                    var seglen = segs[s];
                    builder.AppendSegment(body.Slice(pos, seglen), granule, seglen < 255);
                    pos += seglen;
                }
            }

            if (eos)
                builder.MarkEos();

            pages++;
            offset = bodyOff + bodyLen;
            _ = version;
        }

        var list = new List<XcomStream>();
        foreach (var builder in streams.Values.OrderBy(s => s.Serial))
            list.Add(builder.Build());

        return new XcomFile(displayName, pages, list);
    }

    private static int IndexOfCapture(ReadOnlySpan<byte> data, int start)
    {
        // "OggS"
        ReadOnlySpan<byte> pat = "OggS"u8;
        for (var i = start; i <= data.Length - 4; i++)
        {
            if (data[i] == pat[0] && data[i + 1] == pat[1] && data[i + 2] == pat[2] && data[i + 3] == pat[3])
                return i;
        }

        return -1;
    }

    private sealed class StreamBuilder
    {
        private readonly List<XcomPacket> _packets = new();
        private readonly List<byte> _buf = new();
        private string _bos = string.Empty;
        private bool _eos;
        private int _auxRemaining = -1;
        private long _firstGranule = -1;
        private long _lastGranule = -1;
        private uint _lastPageSeq;

        public StreamBuilder(uint serial) => Serial = serial;

        public uint Serial { get; }

        public void SetBos(string text)
        {
            _bos = text;
            _auxRemaining = ParseHeadersCount(text);
        }

        public void MarkEos() => _eos = true;

        public void NotePage(uint pageSeq, long granule, byte headerType)
        {
            _lastPageSeq = pageSeq;
            if (granule != -1)
            {
                if (_firstGranule < 0)
                    _firstGranule = granule;
                _lastGranule = granule;
            }

            _ = headerType;
        }

        public void AppendSegment(ReadOnlySpan<byte> chunk, long granule, bool complete)
        {
            for (var i = 0; i < chunk.Length; i++)
                _buf.Add(chunk[i]);

            if (!complete)
                return;

            var payload = _buf.ToArray();
            _buf.Clear();

            if (_auxRemaining > 0)
            {
                _packets.Add(new XcomPacket(XcomPacketKind.AuxHeader, granule, payload, TimePrefixNs: null));
                _auxRemaining--;
                return;
            }

            // Footer: codec id text like "[OxTS-NCom]"
            if (payload.Length > 0 && payload[0] == (byte)'[')
            {
                _packets.Add(new XcomPacket(XcomPacketKind.Footer, granule, payload, TimePrefixNs: null));
                return;
            }

            long? timeNs = null;
            byte[] codec;
            if (payload.Length >= DataPacketTimePrefixBytes)
            {
                timeNs = BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(0, 8));
                codec = payload.AsSpan(8).ToArray();
            }
            else
            {
                codec = payload;
            }

            _packets.Add(new XcomPacket(XcomPacketKind.Data, granule, codec, timeNs));
        }

        public XcomStream Build()
        {
            var kind = DetectKind(_bos);
            var label = BuildLabel(_bos, Serial);
            return new XcomStream(
                Serial,
                kind,
                _bos,
                label,
                _packets,
                _firstGranule,
                _lastGranule,
                _eos,
                _lastPageSeq);
        }

        private static int ParseHeadersCount(string bos)
        {
            // Manual: key=value after capture pattern; separators are 00h-1Fh.
            // Real files often put "[OxTS-NCom]headers=2\n" without a newline after ].
            var idx = bos.IndexOf("headers=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return 0;
            idx += "headers=".Length;
            var end = idx;
            while (end < bos.Length && char.IsDigit(bos[end]))
                end++;
            if (end > idx && int.TryParse(bos.AsSpan(idx, end - idx), out var n))
                return Math.Max(0, n);
            return 0;
        }

        private static XcomStreamKind DetectKind(string bos)
        {
            if (bos.Contains("[OxTS-NCom]", StringComparison.OrdinalIgnoreCase))
                return XcomStreamKind.Ncom;
            if (bos.Contains("[OxTS-RCom]", StringComparison.OrdinalIgnoreCase))
                return XcomStreamKind.Rcom;
            if (bos.Contains("[OxTS-MCom]", StringComparison.OrdinalIgnoreCase))
                return XcomStreamKind.Mcom;
            if (bos.Contains("[OxTS-Can]", StringComparison.OrdinalIgnoreCase))
                return XcomStreamKind.Can;
            if (bos.Contains("[OxTS-RD]", StringComparison.OrdinalIgnoreCase))
                return XcomStreamKind.Rd;
            return XcomStreamKind.Unknown;
        }

        private static string BuildLabel(string bos, uint serial)
        {
            _ = bos;
            return $"s{serial}";
        }
    }
}

public enum XcomStreamKind
{
    Unknown,
    Ncom,
    Rcom,
    Mcom,
    Can,
    Rd
}

public enum XcomPacketKind
{
    AuxHeader,
    Data,
    Footer
}

public sealed record XcomPacket(
    XcomPacketKind Kind,
    long GranuleNs,
    byte[] Payload,
    long? TimePrefixNs);

public sealed class XcomStream
{
    public XcomStream(
        uint serial,
        XcomStreamKind kind,
        string bosText,
        string label,
        IReadOnlyList<XcomPacket> packets,
        long firstGranuleNs,
        long lastGranuleNs,
        bool sawEos,
        uint lastPageSequence)
    {
        Serial = serial;
        Kind = kind;
        BosText = bosText;
        Label = label;
        Packets = packets;
        FirstGranuleNs = firstGranuleNs;
        LastGranuleNs = lastGranuleNs;
        SawEos = sawEos;
        LastPageSequence = lastPageSequence;
    }

    public uint Serial { get; }
    public XcomStreamKind Kind { get; }
    public string BosText { get; }
    public string Label { get; set; }
    public IReadOnlyList<XcomPacket> Packets { get; }
    public long FirstGranuleNs { get; }
    public long LastGranuleNs { get; }
    public bool SawEos { get; }
    public uint LastPageSequence { get; }

    public IEnumerable<XcomPacket> DataPackets =>
        Packets.Where(p => p.Kind == XcomPacketKind.Data);

    public IEnumerable<XcomPacket> AuxHeaders =>
        Packets.Where(p => p.Kind == XcomPacketKind.AuxHeader);

    public string? FindAuxValue(string key)
    {
        foreach (var aux in AuxHeaders)
        {
            var text = Encoding.ASCII.GetString(aux.Payload);
            foreach (var line in text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = line.IndexOf('=');
                if (idx <= 0)
                    continue;
                if (line.AsSpan(0, idx).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    return line[(idx + 1)..].Trim();
            }
        }

        return null;
    }
}

public sealed class XcomFile
{
    public XcomFile(string displayName, int pageCount, IReadOnlyList<XcomStream> streams)
    {
        DisplayName = displayName;
        PageCount = pageCount;
        Streams = streams;
    }

    public string DisplayName { get; }
    public int PageCount { get; }
    public IReadOnlyList<XcomStream> Streams { get; }
}
