using Conesoft.Files;
using Conesoft.Hosting;
using Microsoft.Extensions.Configuration;
using Serilog;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
builder.Services
    .AddPeriodicGarbageCollection(TimeSpan.FromMinutes(5));

var host = builder.Build();

await host.StartAsync();

var configuration = new ConfigurationBuilder().AddJsonFile(Host.GlobalConfiguration.Path).Build();

var file = Host.GlobalStorage / "FromSources" / "Ipify" / Filename.From("Ip", "txt");
file.Parent.Create();

var ipify = new Conesoft.Ipify.Client(new HttpClient());
var dnsimple = new Conesoft.DNSimple.Client(new HttpClient());

var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
do
{
    try
    {
        var lastIp = file.Exists ? IPAddress.Parse(await file.ReadText()) : IPAddress.None;

        var currentIp = await ipify.GetPublicIPAddress();

        if (currentIp.ToString() != lastIp.ToString()) // urgh, value comparison, the cheap way
        {
            Log.Information("updating IP Address to {new}", currentIp);
            await UpdateDnsRecord(currentIp);
            await file.WriteText(currentIp.ToString());
        }
    }
    catch (Exception e)
    {
        Log.Error("error: {exception}", e.Message);
    }
}
while (await timer.WaitForNextTickAsync());

    async Task UpdateDnsRecord(IPAddress address)
{
    dnsimple.UseToken(configuration["hosting:dnsimple-token"]);

    var account = (await dnsimple.GetAccounts()).First();

    var zones = await account.GetZones();

    foreach (var zone in zones)
    {

        var hosted = Host.Root / "Deployments" / "Websites" / Filename.From(zone.Name, "zip");
        if (hosted.Exists)
        {
            Log.Information("Updating Zone {zone}", zone.Name);
            var record = await zone.GetRecord(Conesoft.DNSimple.RecordType.A);
            await record.Update(address.ToString());
        }
    }
}