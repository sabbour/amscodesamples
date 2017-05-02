using Microsoft.WindowsAzure.MediaServices.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace BulkIngest
{
    public static class AMSHelper
    {
        public static void SetFileAsPrimary(IAsset asset, string assetfilename) {

            var ismAssetFiles = asset.AssetFiles.ToList().Where(f => f.Name.Equals(assetfilename, StringComparison.OrdinalIgnoreCase)).ToArray();


            if (ismAssetFiles.Count() == 1)
            {
                try
                {
                    // let's remove primary attribute to another file if any
                    asset.AssetFiles.Where(af => af.IsPrimary).ToList().ForEach(af => { af.IsPrimary = false; af.Update(); });
                    ismAssetFiles.First().IsPrimary = true;
                    ismAssetFiles.First().Update();
                }
                catch
                {
                    throw;
                }
            }
        }

        public static ManifestGenerated LoadAndUpdateManifestTemplate(IAsset asset)
        {
            var mp4AssetFiles = asset.AssetFiles.ToList().Where(f => f.Name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)).ToArray();
            var m4aAssetFiles = asset.AssetFiles.ToList().Where(f => f.Name.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)).ToArray();
            var mediaAssetFiles = asset.AssetFiles.ToList().Where(f => f.Name.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || f.Name.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase)).ToArray();

            if (mp4AssetFiles.Count() != 0 || m4aAssetFiles.Count() != 0)
            {
                // Prepare the manifest
                XDocument doc = XDocument.Load("Manifest.ism");
               
                XNamespace ns = "http://www.w3.org/2001/SMIL20/Language";

                var bodyxml = doc.Element(ns + "smil");
                var body2 = bodyxml.Element(ns + "body");

                var switchxml = body2.Element(ns + "switch");

                // video tracks
                foreach (var file in mp4AssetFiles)
                {
                    switchxml.Add(new XElement(ns + "video", new XAttribute("src", file.Name)));
                }

                // audio tracks (m4a)
                foreach (var file in m4aAssetFiles)
                {
                    switchxml.Add(new XElement(ns + "audio", new XAttribute("src", file.Name), new XAttribute("title", Path.GetFileNameWithoutExtension(file.Name))));
                }

                if (m4aAssetFiles.Count() == 0)
                {
                    // audio track
                    var mp4AudioAssetFilesName = mp4AssetFiles.Where(f =>
                                                                (f.Name.ToLower().Contains("audio") && !f.Name.ToLower().Contains("video"))
                                                                ||
                                                                (f.Name.ToLower().Contains("aac") && !f.Name.ToLower().Contains("h264"))
                                                                );

                    var mp4AudioAssetFilesSize = mp4AssetFiles.OrderBy(f => f.ContentFileSize);

                    string mp4fileaudio = (mp4AudioAssetFilesName.Count() == 1) ? mp4AudioAssetFilesName.FirstOrDefault().Name : mp4AudioAssetFilesSize.FirstOrDefault().Name; // if there is one file with audio or AAC in the name then let's use it for the audio track
                    switchxml.Add(new XElement(ns + "audio", new XAttribute("src", mp4fileaudio), new XAttribute("title", "audioname")));
                }

                // manifest filename
                string name = CommonPrefix(mediaAssetFiles.Select(f => Path.GetFileNameWithoutExtension(f.Name)).ToArray());
                if (string.IsNullOrEmpty(name))
                {
                    name = "manifest";
                }
                else if (name.EndsWith("_") && name.Length > 1) // i string ends with "_", let's remove it
                {
                    name = name.Substring(0, name.Length - 1);
                }
                name = name + ".ism";

                return new ManifestGenerated() { Content = doc.Declaration.ToString() + Environment.NewLine + doc.ToString(), FileName = name };

            }
            else
            {
                return new ManifestGenerated() { Content = null, FileName = string.Empty }; // no mp4 in asset
            }
        }

        public class ManifestGenerated
        {
            public string FileName;
            public string Content;
        }

        private static string CommonPrefix(string[] ss)
        {
            if (ss.Length == 0)
            {
                return "";
            }

            if (ss.Length == 1)
            {
                return ss[0];
            }

            int prefixLength = 0;

            foreach (char c in ss[0])
            {
                foreach (string s in ss)
                {
                    if (s.Length <= prefixLength || s[prefixLength] != c)
                    {
                        return ss[0].Substring(0, prefixLength);
                    }
                }
                prefixLength++;
            }

            return ss[0]; // all strings identical
        }

    }
}
