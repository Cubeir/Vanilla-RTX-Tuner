
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Vanilla_RTX_Tuner_WinUI.Core;
public class PackLocator
{
    public static string vanillaRTXHeaderUUID = Helpers.GetConfig<string>("vanilla_rtx_uuid_header");
    public static string vanillaRTXModuleUUID = Helpers.GetConfig<string>("vanilla_rtx_uuid_module");
    public static string vanillaRTXNormalsHeaderUUID = Helpers.GetConfig<string>("vanilla_rtx_normals_uuid_header");
    public static string vanillaRTXNormalsModuleUUID = Helpers.GetConfig<string>("vanilla_rtx_normals_uuid_module");
    public static string vanillaRTXOpusHeaderUUID = Helpers.GetConfig<string>("vanilla_rtx_opus_uuid_header");
    public static string vanillaRTXOpusModuleUUID = Helpers.GetConfig<string>("vanilla_rtx_opus_uuid_module");

    public static string LocatePacks(bool isTargetingPreview,
    out string vanillaRTXLocation, out string vanillaRTXVersion,
    out string vanillaRTXNormalsLocation, out string vanillaRTXNormalsVersion,
    out string vanillaRTXOpusLocation, out string vanillaRTXOpusVersion)
    {
        // Out parameters
        vanillaRTXLocation = string.Empty;
        vanillaRTXVersion = string.Empty;
        vanillaRTXNormalsLocation = string.Empty;
        vanillaRTXNormalsVersion = string.Empty;
        vanillaRTXOpusLocation = string.Empty;
        vanillaRTXOpusVersion = string.Empty;

        try
        {
            // Find the correct Minecraft package folder (pattern-based, like Updater)
            var packagesRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            string mcFolderPattern = isTargetingPreview
                ? "Microsoft.MinecraftWindowsBeta_"
                : "Microsoft.MinecraftUWP_";

            var mcRoot = Directory.GetDirectories(packagesRoot, mcFolderPattern + "*").FirstOrDefault();
            if (mcRoot == null)
            {
                return "Resource pack directory not found ❌ is the correct version of Minecraft installed?";
            }

            var resolvedPath = Path.Combine(mcRoot, "LocalState", "games", "com.mojang", "resource_packs");
            if (!Directory.Exists(resolvedPath))
            {
                return "Resource pack directory not found ❌ is the correct version of Minecraft installed?";
            }

            var manifestFiles = Directory.GetFiles(resolvedPath, "manifest.json", SearchOption.AllDirectories);
            var results = new List<string>();

            // Track latest version for each pack type
            (string path, int[] version)? latestVanillaRTX = null;
            (string path, int[] version)? latestVanillaRTXNormals = null;
            (string path, int[] version)? latestVanillaRTXOpus = null;

            int[] MinVersion = new int[] { 1, 21, 150 };

            static int CompareVersion(int[] a, int[] b)
            {
                for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
                {
                    int va = i < a.Length ? a[i] : 0;
                    int vb = i < b.Length ? b[i] : 0;
                    if (va > vb) return 1;
                    if (va < vb) return -1;
                }
                return 0;
            }

            foreach (var file in manifestFiles)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    dynamic data = JsonConvert.DeserializeObject(json);

                    string headerUUID = data?.header?.uuid;
                    string moduleUUID = data?.modules?[0]?.uuid;
                    string folder = Path.GetDirectoryName(file)!;
                    var verArray = data?.header?.version;
                    int[] version = new int[] {
                    (int)(verArray?[0] ?? 0),
                    (int)(verArray?[1] ?? 0),
                    (int)(verArray?[2] ?? 0)
                };

                    // Only consider packs >= 1.21.150
                    if (CompareVersion(version, MinVersion) < 0)
                        continue;

                    if (string.Equals(headerUUID, vanillaRTXHeaderUUID, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(moduleUUID, vanillaRTXModuleUUID, StringComparison.OrdinalIgnoreCase))
                    {
                        if (latestVanillaRTX == null || CompareVersion(version, latestVanillaRTX.Value.version) > 0)
                            latestVanillaRTX = (folder, version);
                    }
                    else if (string.Equals(headerUUID, vanillaRTXNormalsHeaderUUID, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(moduleUUID, vanillaRTXNormalsModuleUUID, StringComparison.OrdinalIgnoreCase))
                    {
                        if (latestVanillaRTXNormals == null || CompareVersion(version, latestVanillaRTXNormals.Value.version) > 0)
                            latestVanillaRTXNormals = (folder, version);
                    }
                    else if (string.Equals(headerUUID, vanillaRTXOpusHeaderUUID, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(moduleUUID, vanillaRTXOpusModuleUUID, StringComparison.OrdinalIgnoreCase))
                    {
                        if (latestVanillaRTXOpus == null || CompareVersion(version, latestVanillaRTXOpus.Value.version) > 0)
                            latestVanillaRTXOpus = (folder, version);
                    }
                }
                catch
                {
                    results.Add("Malformed manifest.");
                }
            }

            // Set out parameters and results
            if (latestVanillaRTX != null)
            {
                vanillaRTXLocation = latestVanillaRTX.Value.path;
                vanillaRTXVersion = $"v{latestVanillaRTX.Value.version[0]}.{latestVanillaRTX.Value.version[1]}.{latestVanillaRTX.Value.version[2]}";
                results.Add($"✅ Found: Vanilla RTX — {vanillaRTXVersion}");
            }
            else
            {
                results.Add("⚠️ Not found: Vanilla RTX");
            }

            if (latestVanillaRTXNormals != null)
            {
                vanillaRTXNormalsLocation = latestVanillaRTXNormals.Value.path;
                vanillaRTXNormalsVersion = $"v{latestVanillaRTXNormals.Value.version[0]}.{latestVanillaRTXNormals.Value.version[1]}.{latestVanillaRTXNormals.Value.version[2]}";
                results.Add($"✅ Found: Vanilla RTX Normals — {vanillaRTXNormalsVersion}");
            }
            else
            {
                results.Add("⚠️ Not found: Vanilla RTX Normals");
            }

            if (latestVanillaRTXOpus != null)
            {
                vanillaRTXOpusLocation = latestVanillaRTXOpus.Value.path;
                vanillaRTXOpusVersion = $"v{latestVanillaRTXOpus.Value.version[0]}.{latestVanillaRTXOpus.Value.version[1]}.{latestVanillaRTXOpus.Value.version[2]}";
                results.Add($"✅ Found: Vanilla RTX Opus — {vanillaRTXOpusVersion}");
            }
            else
            {
                results.Add("⚠️ Not found: Vanilla RTX Opus");
            }

            return string.Join(Environment.NewLine, results);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}