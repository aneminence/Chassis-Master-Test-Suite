using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Chassis_Master_Test_Suite.Communication.Remote;

/// <summary>Bounded DNS-SD query. Results are untrusted candidates, never authorization.</summary>
public static class CMTSMdnsDiscovery
{
    private const string Service = "_cmts._tcp.local";

    public static async Task<IReadOnlyList<(string Host, int Port, string Path)>> DiscoverAsync(
        TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        var addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.Address).Distinct().Take(16).ToArray();
        var tasks = addresses.Select(a => QueryAsync(a, deadline.Token)).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return results.SelectMany(x => x).Distinct().ToArray();
    }

    private static async Task<List<(string Host, int Port, string Path)>> QueryAsync(IPAddress local, CancellationToken ct)
    {
        var result = new List<(string Host, int Port, string Path)>();
        try
        {
            using var udp = new UdpClient(new IPEndPoint(local, 0));
            // QU requests unicast replies to the ephemeral source port; no listener/firewall rule on 5353.
            var query = BuildQuery();
            await udp.SendAsync(query, new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353), ct).ConfigureAwait(false);
            var hosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var services = new Dictionary<string, (string Target, int Port)>(StringComparer.OrdinalIgnoreCase);
            var instances = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (!ct.IsCancellationRequested)
            {
                var packet = await udp.ReceiveAsync(ct).ConfigureAwait(false);
                if (packet.RemoteEndPoint.Port != 5353) continue;
                Parse(packet.Buffer, hosts, services, instances);
                foreach (var missing in MissingQueries(hosts, services, instances, queried))
                    await udp.SendAsync(BuildQuery(missing.Name, missing.Type), new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353), ct).ConfigureAwait(false);
                foreach (var name in instances)
                    if (services.TryGetValue(name, out var srv) && hosts.TryGetValue(srv.Target, out var host))
                        if (!result.Contains((host, srv.Port, "/cmts/v1"))) result.Add((host, srv.Port, "/cmts/v1"));
                if (result.Count >= 64) break;
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
        return result.Distinct().ToList();
    }

    internal static IReadOnlyList<(string Name, ushort Type)> MissingQueries(
        Dictionary<string,string> hosts, Dictionary<string,(string Target,int Port)> services,
        HashSet<string> instances, HashSet<string> queried)
    {
        var pending = new List<(string, ushort)>();
        foreach (var instance in instances)
        {
            if (!services.TryGetValue(instance, out var service)) Add(instance, 33);
            else if (!hosts.ContainsKey(service.Target)) Add(service.Target, 1);
        }
        return pending;
        void Add(string name, ushort type)
        {
            if (queried.Count < 64 && queried.Add($"{type}:{name}")) pending.Add((name,type));
        }
    }

    internal static byte[] BuildQuery(string name = Service, ushort type = 12)
    {
        var bytes = new List<byte>(new byte[] { 0,0,0,0,0,1,0,0,0,0,0,0 });
        foreach (var label in name.Split('.')) { var encoded = Encoding.UTF8.GetBytes(label); if (encoded.Length is 0 or > 63) throw new ArgumentException("Invalid DNS label"); bytes.Add((byte)encoded.Length); bytes.AddRange(encoded); }
        bytes.AddRange(new byte[] { 0,(byte)(type >> 8),(byte)type,0x80,1 });
        return bytes.ToArray();
    }

    internal static void Parse(byte[] data, Dictionary<string,string> hosts,
        Dictionary<string,(string Target,int Port)> services, HashSet<string> instances)
    {
        try
        {
            if (data.Length < 12 || (data[2] & 0x80) == 0) return;
            var pos = 12;
            var questions = U16(data, 4);
            var count = U16(data, 6) + U16(data, 8) + U16(data, 10);
            if (questions > 64 || count > 256) return;
            for (var i=0;i<questions;i++) { Name(data, ref pos); pos += 4; }
            for (var i=0;i<count;i++)
            {
                var name = Name(data, ref pos);
                var type = U16(data,pos); var cls = U16(data,pos+2) & 0x7fff;
                var length = U16(data,pos+8); pos += 10;
                var end = checked(pos+length);
                if (end > data.Length) return;
                if (cls == 1)
                {
                    if (type == 1 && length == 4 && hosts.Count < 256)
                        hosts[name] = new IPAddress(data.AsSpan(pos,4)).ToString();
                    else if (type == 12 && name.Equals(Service,StringComparison.OrdinalIgnoreCase) && instances.Count < 64)
                    {
                        var p=pos; var instance=Name(data,ref p);
                        if (p <= end && instance.EndsWith("."+Service,StringComparison.OrdinalIgnoreCase)) instances.Add(instance);
                    }
                    else if (type == 33 && length >= 7 && services.Count < 128)
                    {
                        var port=U16(data,pos+4); var p=pos+6; var target=Name(data,ref p);
                        if (port>0 && p<=end) services[name]=(target,port);
                    }
                }
                pos=end;
            }
        }
        catch (ArgumentException) { }
        catch (IndexOutOfRangeException) { }
        catch (OverflowException) { }
    }

    private static int U16(byte[] data,int p)
    {
        if (p<0 || p+2>data.Length) throw new ArgumentException("Truncated DNS record");
        return (data[p]<<8)|data[p+1];
    }
    private static string Name(byte[] data,ref int position)
    {
        var p=position; var jumped=false; var labels=new List<string>(); var seen=new HashSet<int>(); var total=0;
        while(true)
        {
            if (p<0 || p>=data.Length || !seen.Add(p)) throw new ArgumentException("Invalid DNS name");
            var length=data[p++];
            if ((length & 0xc0)==0xc0)
            {
                if(p>=data.Length) throw new ArgumentException("Truncated DNS pointer");
                var target=((length&0x3f)<<8)|data[p++];
                if(!jumped) position=p;
                jumped=true; p=target; continue;
            }
            if(length==0) { if(!jumped) position=p; return string.Join('.',labels); }
            if(length>63 || p+length>data.Length || (total+=length+1)>255) throw new ArgumentException("Invalid DNS label");
            labels.Add(Encoding.UTF8.GetString(data,p,length)); p+=length;
        }
    }
}
