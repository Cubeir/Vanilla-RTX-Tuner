
using System;
using System.Collections.Generic;
using System.IO;
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
            var resolvedPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages",
                isTargetingPreview ? "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe" : "Microsoft.MinecraftUWP_8wekyb3d8bbwe",
                "LocalState",
                "games",
                "com.mojang",
                "resource_packs"
            );

            if (!Directory.Exists(resolvedPath))
            {
                return "Resource pack directory not found, is the correct version of Minecraft installed?";
            }

            var manifestFiles = Directory.GetFiles(resolvedPath, "manifest.json", SearchOption.AllDirectories);
            var results = new List<string>();

            foreach (var file in manifestFiles)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    dynamic data = JsonConvert.DeserializeObject(json);

                    string headerUUID = data?.header?.uuid;
                    string moduleUUID = data?.modules?[0]?.uuid;
                    string folder = Path.GetDirectoryName(file)!;

                    static string FormatVersion(dynamic verArray)
                    {
                        try
                        {
                            int major = verArray[0], minor = verArray[1], patch = verArray[2];
                            return $"v{major}.{minor}.{patch}";
                        }
                        catch
                        {
                            return "";
                        }
                    }

                    string version = FormatVersion(data?.header?.version);

                    if (string.Equals(headerUUID, vanillaRTXHeaderUUID, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(moduleUUID, vanillaRTXModuleUUID, StringComparison.OrdinalIgnoreCase))
                    {
                        vanillaRTXLocation = folder;
                        vanillaRTXVersion = version;
                        results.Add($"Found: Vanilla RTX — {version}");
                    }
                    else if (string.Equals(headerUUID, vanillaRTXNormalsHeaderUUID, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(moduleUUID, vanillaRTXNormalsModuleUUID, StringComparison.OrdinalIgnoreCase))
                    {
                        vanillaRTXNormalsLocation = folder;
                        vanillaRTXNormalsVersion = version;
                        results.Add($"Found: Vanilla RTX Normals — {version}");
                    }
                    else if (string.Equals(headerUUID, vanillaRTXOpusHeaderUUID, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(moduleUUID, vanillaRTXOpusModuleUUID, StringComparison.OrdinalIgnoreCase))
                    {
                        vanillaRTXOpusLocation = folder;
                        vanillaRTXOpusVersion = version;
                        results.Add($"Found: Vanilla RTX Opus — {version}");
                    }

                    // Break early if all packs found
                    if (!string.IsNullOrEmpty(vanillaRTXLocation) &&
                        !string.IsNullOrEmpty(vanillaRTXNormalsLocation) &&
                        !string.IsNullOrEmpty(vanillaRTXOpusLocation))
                    {
                        break;
                    }
                }
                catch
                {
                    results.Add("Malformed manifest.");
                }
            }

            // Add not found messages
            if (string.IsNullOrEmpty(vanillaRTXLocation))
                results.Add("Not found: Vanilla RTX");
            if (string.IsNullOrEmpty(vanillaRTXNormalsLocation))
                results.Add("Not found: Vanilla RTX Normals");
            if (string.IsNullOrEmpty(vanillaRTXOpusLocation))
                results.Add("Not found: Vanilla RTX Opus");

            return string.Join(Environment.NewLine, results);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}