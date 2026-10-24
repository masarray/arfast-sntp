using System.Net;
using System.Net.Sockets;
using ARFastSNTP;
using ARFastSNTP.Linux;
static void Check(bool ok, string why) { if (!ok) throw new Exception("Regression: " + why); }
Check(Catalog.ValidIp("192.0.2.42"), "valid IP");
Check(!Catalog.ValidIp("127.1"), "reject shorthand");
Check(!Catalog.ValidIp("224.0.0.1"), "reject multicast");
Check(!Catalog.ValidIp("0.0.0.0"), "reject unspecified");
var coherent = new [] {
 new SntpSample(TimeSpan.FromMilliseconds(110), 4, 2),
 new SntpSample(TimeSpan.FromMilliseconds(120), 5, 2),
 new SntpSample(TimeSpan.FromMilliseconds(130), 6, 2) };
Check(TrustedSourcePolicy.Choose(coherent)==TimeSpan.FromMilliseconds(120),"median");
Check(TrustedSourcePolicy.Choose(coherent.Take(2).ToArray()) is null,"3 independent samples");
Check(TrustedSourcePolicy.Choose(new [] { coherent[0],coherent[1],
 new SntpSample(TimeSpan.FromSeconds(2),5,2)}) is null,"disagreement rejects");
Check(TrustedSourcePolicy.Choose(new [] { coherent[0],coherent[1],
 new SntpSample(TimeSpan.FromMilliseconds(125),251,2)}) is null,"RTT rejected");
string state=Path.Combine(Path.GetTempPath(),"arfast-test-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(state);
Environment.SetEnvironmentVariable("ARFASTSNTP_STATE_DIR",state);
try {
 Catalog.Change("--add","192.0.2.10");
 Catalog.Change("--add","192.0.2.11");
 Catalog.Change("--disable","192.0.2.11");
 Check(Catalog.Read().Length==2 && !Catalog.Read()[1].Enabled,"disable");
 Catalog.Change("--remove","192.0.2.11");
 Check(Catalog.Read().Length==1,"remove");
} finally {
 Environment.SetEnvironmentVariable("ARFASTSNTP_STATE_DIR",null);
 Directory.Delete(state,true);
}
using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback,0));
int port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
Task reply=Task.Run(async ()=>{
 UdpReceiveResult request=await server.ReceiveAsync(timeout.Token);
 byte[] packet=new byte[48];
 packet[0]=0x24;
 packet[1]=2;
 Array.Copy(request.Buffer,40,packet,24,8);
 var now=DateTimeOffset.UtcNow;
 NtpProtocol.WriteTimestamp(packet.AsSpan(32,8),now);
 NtpProtocol.WriteTimestamp(packet.AsSpan(40,8),now.AddMilliseconds(1));
 await server.SendAsync(packet,request.RemoteEndPoint,timeout.Token);
},timeout.Token);
var sample=await NtpProtocol.QueryAsync(IPAddress.Loopback,2026,timeout.Token,port);
await reply;
Check(sample.Stratum==2 && sample.RttMs>=0 && sample.RttMs<250,"real UDP fixture");
byte[] req=new byte[48],invalid=new byte[48];
req[40]=1; // Different transaction ID: should fail originate binding.
invalid[0]=0x24;invalid[1]=2;
var t=DateTimeOffset.UtcNow;
NtpProtocol.WriteTimestamp(invalid.AsSpan(32,8),t);
NtpProtocol.WriteTimestamp(invalid.AsSpan(40,8),t);
bool rejected=false;
try { _=NtpProtocol.Parse(req,invalid,t,t.AddMilliseconds(2),2026); }
catch(InvalidOperationException) { rejected=true; }
Check(rejected,"reject spoofed originate");
Console.WriteLine("Linux SNTP regression passed.");
