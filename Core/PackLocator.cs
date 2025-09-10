﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Vanilla_RTX_Tuner_WinUI.Core;

// TODO: Utility methods for this class to identify type, belonging, version, and location of add-ons and extensions to pass them for tuning.
public class PackLocator
{
    public const string VANILLA_RTX_HEADER_UUID = "a5c3cc7d-1740-4b5e-ae2c-71bc14b3f63b";
    public const string VANILLA_RTX_MODULE_UUID = "af805084-fafa-4124-9ae2-00be4bc202dc";
    public const string VANILLA_RTX_NORMALS_HEADER_UUID = "bbe2b225-b45b-41c2-bd3b-465cd83e6071";
    public const string VANILLA_RTX_NORMALS_MODULE_UUID = "b2eef2c6-d893-467e-b31d-cda7bf643eaa";
    public const string VANILLA_RTX_OPUS_HEADER_UUID = "7c87f859-4d79-4d51-8887-bf450b2b2bfa";
    public const string VANILLA_RTX_OPUS_MODULE_UUID = "be0b22f0-ad13-4bbd-81ba-b457fd9e38b8";

    // Change the minimum version of pack detected by Tuner
    private static readonly int[] MinVersion = new int[] { 1, 21, 150 };

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
            string basePath, versionName;

            if (isTargetingPreview)
            {
                basePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Minecraft Bedrock Preview"
                );
                versionName = "Minecraft Preview";
            }
            else
            {
                basePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Minecraft Bedrock"
                );
                versionName = "Minecraft";
            }

            var resolvedPath = Path.Combine(basePath, "Users", "Shared", "games", "com.mojang", "resource_packs");

            if (!Directory.Exists(resolvedPath))
            {
                return $"Resource pack directory not found ❌ is the correct version of {versionName} installed?";
            }

            var manifestFiles = Directory.GetFiles(resolvedPath, "manifest.json", SearchOption.AllDirectories);
            var results = new List<string>();

            // Track latest version for each pack type
            (string path, int[] version)? latestVanillaRTX = null;
            (string path, int[] version)? latestVanillaRTXNormals = null;
            (string path, int[] version)? latestVanillaRTXOpus = null;

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

                    if (string.Equals(headerUUID, VANILLA_RTX_HEADER_UUID, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(moduleUUID, VANILLA_RTX_MODULE_UUID, StringComparison.OrdinalIgnoreCase))
                    {
                        if (latestVanillaRTX == null || CompareVersion(version, latestVanillaRTX.Value.version) > 0)
                            latestVanillaRTX = (folder, version);
                    }
                    else if (string.Equals(headerUUID, VANILLA_RTX_NORMALS_HEADER_UUID, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(moduleUUID, VANILLA_RTX_NORMALS_MODULE_UUID, StringComparison.OrdinalIgnoreCase))
                    {
                        if (latestVanillaRTXNormals == null || CompareVersion(version, latestVanillaRTXNormals.Value.version) > 0)
                            latestVanillaRTXNormals = (folder, version);
                    }
                    else if (string.Equals(headerUUID, VANILLA_RTX_OPUS_HEADER_UUID, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(moduleUUID, VANILLA_RTX_OPUS_MODULE_UUID, StringComparison.OrdinalIgnoreCase))
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