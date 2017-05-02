using Microsoft.WindowsAzure.MediaServices.Client;
using Microsoft.WindowsAzure.MediaServices.Client.DynamicEncryption;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Live
{
    class Program
    {
        static void Main(string[] args)
        {
            var context = new CloudMediaContext(new MediaServicesCredentials(ConfigurationManager.AppSettings["accountName"], ConfigurationManager.AppSettings["accountKey"]));

            // Get channel
            var channel = context.Channels.ToList().Where(c => c.Id == "nb:chid:UUID:16fe4028-c3e8-48cd-a23e-f1cc230cd203").FirstOrDefault();

            // Create the program's asset
            IAsset asset = context.Assets.Create("epgprogram1-asset", AssetCreationOptions.None);
            
            var policy = context.AssetDeliveryPolicies.Create("Clear Policy",
                AssetDeliveryPolicyType.NoDynamicEncryption,
                AssetDeliveryProtocol.HLS | AssetDeliveryProtocol.SmoothStreaming | AssetDeliveryProtocol.Dash, null);
            asset.DeliveryPolicies.Add(policy);

            // Start a program
            var epgprogram1 = channel.Programs.Create("epgprogram1", new TimeSpan(1, 10, 0), asset.Id);
            epgprogram1.Start();
            var epgProgram1Id = epgprogram1.Id;

            // Sleep 1 minute then stop the program
            Thread.Sleep(60 * 1000);

            // Stop the program
            epgprogram1 = channel.Programs.ToList().Where(p => p.Id == epgProgram1Id).FirstOrDefault();
            epgprogram1.Stop();

            // Get programs
            var programs = channel.Programs.ToList();

        }
    }
}
