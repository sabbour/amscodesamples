using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Configuration;
using System.Xml.Linq;
using System.Threading;
using Microsoft.WindowsAzure.MediaServices.Client;

namespace BulkIngest
{
    class Program
    {
        static void Main(string[] args)
        {
            Task t = MainAsync(args);
            t.Wait();
        }

        static async Task MainAsync(string[] args)
        {
            var ingestDirectory = new DirectoryInfo(ConfigurationManager.AppSettings["watchFolder"]);
            var context = new CloudMediaContext(new MediaServicesCredentials(ConfigurationManager.AppSettings["accountName"], ConfigurationManager.AppSettings["accountKey"]));
            var commonId = Guid.NewGuid().ToString();

            // Ask whether to do direct upload or external (bulk) upload
            Console.WriteLine("Watching directory " + ConfigurationManager.AppSettings["watchFolder"]);
            Console.WriteLine("Enter D for direct upload or E for external upload:");
            var mode = Console.ReadLine().ToUpper();

            // 1. Create Asset
            #region 1. Create Asset
            var asset = await context.Assets.CreateAsync(ingestDirectory.Name, AssetCreationOptions.None, CancellationToken.None); // Create asset
            asset.AlternateId = $"custom-id-{commonId}"; // Set the AlternateId to something custom
            asset.Name = $"custom-name-{commonId}"; // Give the Asset a name
            asset.Update();

            Console.WriteLine(string.Format("Asset id :{0}", asset.Id));
            Console.WriteLine(string.Format("Asset AlternateId :{0}", asset.AlternateId));
            #endregion

            if (mode == "D")
            {
                // 2. Upload
                #region 2. Upload
                var blobTransferClient = new BlobTransferClient();
                blobTransferClient.NumberOfConcurrentTransfers = 20;
                blobTransferClient.ParallelTransferThreadCount = 20;

                blobTransferClient.TransferProgressChanged += blobTransferClient_TransferProgressChanged;


                var accessPolicy = await context.AccessPolicies.CreateAsync($"{asset.Name} policy", TimeSpan.FromDays(30), AccessPermissions.Write | AccessPermissions.List);
                var locator = await context.Locators.CreateLocatorAsync(LocatorType.Sas, asset, accessPolicy);


                var filePaths = Directory.EnumerateFiles(ConfigurationManager.AppSettings["watchFolder"]);
                var uploadTasks = new List<Task>();
                foreach (var filePath in filePaths)
                {
                    var assetFile = await asset.AssetFiles.CreateAsync(Path.GetFileName(filePath), CancellationToken.None);
                    Console.WriteLine("Created assetFile {0}", assetFile.Name);

                    Console.WriteLine("Start uploading of {0}", assetFile.Name);
                    uploadTasks.Add(assetFile.UploadAsync(filePath, blobTransferClient, locator, CancellationToken.None));
                }

                await Task.WhenAll(uploadTasks.ToArray());
                Console.WriteLine("Done uploading the files");

                blobTransferClient.TransferProgressChanged -= blobTransferClient_TransferProgressChanged;
                #endregion  
            }
            else if (mode == "E")
            {
                // 2. Create IngestManifest
                #region 2. Create IngestManifest
                var manifest = await context.IngestManifests.CreateAsync($"ingest-{commonId}");
                Console.WriteLine(string.Format("manifest id : {0}", manifest.Id));
                Console.WriteLine(string.Format("manifest BlobStorageUriForUpload: {0}", manifest.BlobStorageUriForUpload));
                #endregion

                // 3. Add the file names to the IngestManifest as assets
                #region 3. Add the file names to the IngestManifest as assets
                // Retrieve files in the directory
                var fileList = new List<string>();
                foreach (var item in ingestDirectory.EnumerateFiles())
                {
                    fileList.Add(item.Name);
                    Console.WriteLine(string.Format("File : {0}", item.Name));
                }

                await manifest.IngestManifestAssets.CreateAsync(asset, fileList.ToArray<string>(), CancellationToken.None);
                #endregion

                // 4. Check external upload progress, this could be an external process as an Azure Function
                #region 4. Check external upload progress, this could be an external process as an Azure Function
                bool isFinished = false;
                while (!isFinished)
                {
                    manifest = context.IngestManifests.Where(m => m.Id == manifest.Id).FirstOrDefault();

                    Console.WriteLine("** Current time - {0}", DateTime.Now.ToLongTimeString());
                    Console.WriteLine("  PendingFilesCount  : {0}", manifest.Statistics.PendingFilesCount);
                    Console.WriteLine("  FinishedFilesCount : {0}", manifest.Statistics.FinishedFilesCount);
                    Console.WriteLine("  {0}% complete.\n", (float)manifest.Statistics.FinishedFilesCount / (float)(manifest.Statistics.FinishedFilesCount + manifest.Statistics.PendingFilesCount) * 100);

                    if (manifest.Statistics.PendingFilesCount == 0)
                    {
                        Console.WriteLine("External upload finished");
                        isFinished = true;
                        break;
                    }

                    if (manifest.Statistics.FinishedFilesCount < manifest.Statistics.PendingFilesCount)
                    {
                        Console.WriteLine("Waiting 5 seconds for external upload to finish..");
                    }

                    await Task.Delay(5000);
                }

                // Clean-up, delete manifest blob
                await manifest.DeleteAsync();
                #endregion                
            }

            // 5. Create the manifest for the uploaded asset and upload it into the asset
            #region 5. Create the manifest for the uploaded asset and upload it into the asset
            Console.WriteLine("Generating .ism file for the asset");
            var smildata = AMSHelper.LoadAndUpdateManifestTemplate(asset);
            var smilXMLDocument = XDocument.Parse(smildata.Content);
            var smildataAssetFile = asset.AssetFiles.Create(smildata.FileName);

            Stream stream = new MemoryStream();  // Create a stream
            smilXMLDocument.Save(stream);      // Save XDocument into the stream
            stream.Position = 0;   // Rewind the stream ready to read from it elsewhere
            smildataAssetFile.Upload(stream);

            // Update the asset to set the primary file as the ism file
            AMSHelper.SetFileAsPrimary(asset, smildata.FileName);
            #endregion

            // 6. Create locators and publish the asset
            #region 6. Create locators and publish the asset
            // Create a 30-day readonly access policy if it doesn't exist
            var policy = context.AccessPolicies.ToList().FirstOrDefault(p => p.Name == "Streaming policy");
            if (policy == null)
                policy = await context.AccessPolicies.CreateAsync("Streaming policy", TimeSpan.FromDays(30), AccessPermissions.Read);

            // Create a locator to the streaming content on an origin. 
            var originLocator = await context.Locators.CreateLocatorAsync(LocatorType.OnDemandOrigin, asset, policy, DateTime.UtcNow.AddMinutes(-5));

            // Create a full URL to the manifest file. Use this for playback
            // in streaming media clients. 
            Console.WriteLine("URL to manifest for client streaming using Smooth Streaming protocol: ");
            Console.WriteLine(asset.GetSmoothStreamingUri());

            Console.WriteLine("URL to manifest for client streaming using HLS protocol: ");
            Console.WriteLine(asset.GetHlsUri());

            Console.WriteLine("URL to manifest for client streaming using MPEG DASH protocol: ");
            Console.WriteLine(asset.GetMpegDashUri());

            Console.WriteLine();
            #endregion

            Console.ReadLine();
        }

        static void blobTransferClient_TransferProgressChanged(object sender, BlobTransferProgressChangedEventArgs e)
        {
            Console.WriteLine("{0}% upload competed for {1}.", e.ProgressPercentage, e.SourceName);
        }
    }
}
