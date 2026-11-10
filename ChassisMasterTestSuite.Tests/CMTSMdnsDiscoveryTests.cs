using Chassis_Master_Test_Suite.Communication.Remote;
using Xunit;
namespace ChassisMasterTestSuite.Tests;
public class CMTSMdnsDiscoveryTests
{
    [Fact]
    public void TruncatedAndCyclicPacketsAreRejectedWithoutThrowing()
    {
        var hosts=new Dictionary<string,string>();
        var services=new Dictionary<string,(string Target,int Port)>();
        var instances=new HashSet<string>();
        for(var size=0;size<100;size++) CMTSMdnsDiscovery.Parse(new byte[size],hosts,services,instances);
        var cycle=new byte[14]; cycle[2]=0x80; cycle[7]=1; cycle[12]=0xc0; cycle[13]=12;
        CMTSMdnsDiscovery.Parse(cycle,hosts,services,instances);
        Assert.Empty(hosts); Assert.Empty(instances);
    }
    [Fact]
    public void QueryRequestsUnicastPtrForCmtsOnly()
    {
        var query=CMTSMdnsDiscovery.BuildQuery();
        Assert.Equal(new byte[]{0,12,0x80,1},query[^4..]);
        Assert.Contains("_cmts",System.Text.Encoding.ASCII.GetString(query));
    }
    [Fact]
    public void ReadsAddressRecordAndIgnoresQueryPackets()
    {
        var packet=new byte[]{0,0,0x80,0,0,0,0,1,0,0,0,0,1,(byte)'x',0,0,1,0,1,0,0,0,120,0,4,192,168,1,2};
        var hosts=new Dictionary<string,string>(); var services=new Dictionary<string,(string Target,int Port)>(); var instances=new HashSet<string>();
        CMTSMdnsDiscovery.Parse(packet,hosts,services,instances);
        Assert.Equal("192.168.1.2",hosts["x"]);
        hosts.Clear(); packet[2]=0;
        CMTSMdnsDiscovery.Parse(packet,hosts,services,instances); Assert.Empty(hosts);
    }

    [Fact]
    public void SplitRecordsTriggerDeduplicatedSrvThenAddressQueries()
    {
        var hosts = new Dictionary<string,string>();
        var services = new Dictionary<string,(string Target,int Port)>();
        var instances = new HashSet<string>();
        var queried = new HashSet<string>();
        const string instance = "phone._cmts._tcp.local";
        CMTSMdnsDiscovery.Parse(Record("_cmts._tcp.local",12,Name(instance)),hosts,services,instances);
        Assert.Equal((instance,(ushort)33),Assert.Single(CMTSMdnsDiscovery.MissingQueries(hosts,services,instances,queried)));
        Assert.Empty(CMTSMdnsDiscovery.MissingQueries(hosts,services,instances,queried));
        CMTSMdnsDiscovery.Parse(Record(instance,33,new byte[]{0,0,0,0,0x22,0x53}.Concat(Name("phone.local")).ToArray()),hosts,services,instances);
        Assert.Equal(("phone.local",(ushort)1),Assert.Single(CMTSMdnsDiscovery.MissingQueries(hosts,services,instances,queried)));
        CMTSMdnsDiscovery.Parse(Record("phone.local",1,new byte[]{192,168,1,2}),hosts,services,instances);
        Assert.Empty(CMTSMdnsDiscovery.MissingQueries(hosts,services,instances,queried));
        Assert.Equal("192.168.1.2",hosts[services[instance].Target]);
        Assert.Equal(8787,services[instance].Port);
    }
    [Fact]
    public void FollowupQueriesHaveGlobalPerInterfaceBound()
    {
        var instances = Enumerable.Range(0,100).Select(i=>$"phone{i}._cmts._tcp.local").ToHashSet();
        var queried = new HashSet<string>();
        Assert.Equal(64,CMTSMdnsDiscovery.MissingQueries(new(),new(),instances,queried).Count);
        Assert.Empty(CMTSMdnsDiscovery.MissingQueries(new(),new(),instances,queried));
    }
    private static byte[] Name(string name) => name.Split('.').SelectMany(s => new[]{(byte)s.Length}.Concat(System.Text.Encoding.ASCII.GetBytes(s))).Append((byte)0).ToArray();
    private static byte[] Record(string name, byte type, byte[] payload) =>
        new byte[]{0,0,0x80,0,0,0,0,1,0,0,0,0}.Concat(Name(name))
            .Concat(new byte[]{0,type,0,1,0,0,0,120,(byte)(payload.Length>>8),(byte)payload.Length}).Concat(payload).ToArray();
}
