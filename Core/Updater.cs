using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Windows.Storage;
using Vanilla_RTX_App.Modules;
using static Vanilla_RTX_App.Core.PackLocator; // For static UUIDs, they are stored there for locating packs

namespace Vanilla_RTX_App.Core;

// Updating logging of classes here to allow it to properly use the actual Logging method with logLevels

/// =====================================================================================================================
/// Only deals with cache, we don't care if user has Vanilla RTX installed or not, we compare versions of cache to remote
/// No cache? download latest, cache outdated? download latest, if there's a cache and the rest fails for whatever the reason, fallback to cache
/// Deployment deletes any pack that matches UUIDs as defined at the begenning of PackLocator class
/// =====================================================================================================================

// TODO: Implement Opus once it is on GH
public class PackUpdater
{
    private const string VANILLA_RTX_MANIFEST_URL = "https://raw.githubusercontent.com/Cubeir/Vanilla-RTX/master/Vanilla-RTX/manifest.json";
    private const string VANILLA_RTX_NORMALS_MANIFEST_URL = "https://raw.githubusercontent.com/Cubeir/Vanilla-RTX/master/Vanilla-RTX-Normals/manifest.json";
    private const string VANILLA_RTX_REPO_ZIPBALL_URL = "https://github.com/Cubeir/Vanilla-RTX/archive/refs/heads/master.zip";

    // Event for progress updates to avoid UI thread blocking
    public event Action<string>? ProgressUpdate;
    private readonly List<string> _logMessages = new();

    // For cooldown of checking for update to avoid spamming github
    private const string LastUpdateCheckKey = "LastPackUpdateCheckTime";
    private static readonly TimeSpan UpdateCooldown = TimeSpan.FromMinutes(90);

    // Locate a folder name and dump its content out, used for enabling enhanced files of Vanilla RTX after its removal.
    public string EnhancementFolderName { get; set; } = "__enhancements";

    // -------------------------------\           /------------------------------------ //
    public async Task<(bool Success, List<string> Logs)> UpdatePacksAsync()
    {
        _logMessages.Clear();

        try
        {
            var cacheInfo = GetCacheInfo();
            if (!cacheInfo.exists || !System.IO.File.Exists(cacheInfo.path))
            {
                return await HandleNoCacheScenario();
            }
            // Either cache location or the file itself were unavailable:
            else
            {
                return await HandleCacheExistsScenario(cacheInfo.path);
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Unexpected error: {ex.Message}");
            return (false, new List<string>(_logMessages));
        }
    }

    // Updating packs splits into two major possible outcomes, the rest is here:
    private async Task<(bool Success, List<string> Logs)> HandleNoCacheScenario()
    {
        LogMessage("📦 Downloading latest version");

        var (downloadSuccess, downloadPath) = await DownloadLatestPackage();
        if (!downloadSuccess || string.IsNullOrEmpty(downloadPath))
        {
            LogMessage("❌ Failed: Download failed and no pre-existing cache is available.");
            return (false, new List<string>(_logMessages));
        }

        SaveCachedZipballPath(downloadPath);
        LogMessage("✅ Download cached for quicker future redeployment");

        var deploySuccess = await DeployPackage(downloadPath);
        return (deploySuccess, new List<string>(_logMessages));
    }
    private async Task<(bool Success, List<string> Logs)> HandleCacheExistsScenario(string cachePath)
    {
        LogMessage("✅ Cache found");

        var needsUpdate = await CheckIfUpdateNeeded(cachePath);

        if (needsUpdate)
        {
            LogMessage("📦 Update available!");
            var (downloadSuccess, downloadPath) = await DownloadLatestPackage();

            if (downloadSuccess && !string.IsNullOrEmpty(downloadPath))
            {
                SaveCachedZipballPath(downloadPath);
                LogMessage("✅ New version downloaded and cached successfully for future redeployment.");

                var deploySuccess = await DeployPackage(downloadPath);
                return (deploySuccess, new List<string>(_logMessages));
            }
            else
            {
                LogMessage("⚠️ Download failed: fell back to older cached version.");
                var deploySuccess = await DeployPackage(cachePath);
                return (deploySuccess, new List<string>(_logMessages));
            }
        }
        else
        {
            // Either update failed, or is on cooldown, or no update is available, whatever the case got no choice, this is the latest we got
            LogMessage("⚠️ Current cached package is the latest available: Redeploying");
            var deploySuccess = await DeployPackage(cachePath);
            return (deploySuccess, new List<string>(_logMessages));
        }
    }



    private async Task<bool> CheckIfUpdateNeeded(string cachePath)
    {
        if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
        {
            LogMessage("🛜 No network available cached version will be reused.");
            return false;
        }

        // Cooldown logic
        var localSettings = ApplicationData.Current.LocalSettings;
        object lastCheckObj = localSettings.Values[LastUpdateCheckKey];
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (lastCheckObj is string lastCheckStr && DateTimeOffset.TryParse(lastCheckStr, out var lastCheck))
        {
            var cooldownEnd = lastCheck + UpdateCooldown;
            if (now < cooldownEnd)
            {
                var minutesLeft = (int)Math.Ceiling((cooldownEnd - now).TotalMinutes);
                LogMessage($"⏳ Skipped update check.\nCooldown ends in: {minutesLeft} minute{(minutesLeft == 1 ? "" : "s")}");
                return false; // Use cache, skip remote check
            }
        }

        try
        {
            var remoteManifests = await FetchRemoteManifests();

            // Save the check time only if we actually attempted a remote check
            localSettings.Values[LastUpdateCheckKey] = now.ToString("o");

            if (remoteManifests == null)
            {
                LogMessage("Failed to fetch remote manifests: forcing cache update.");
                return true;
            }

            var cacheNeedsUpdate = await DoesCacheNeedUpdate(cachePath, remoteManifests.Value);
            return cacheNeedsUpdate;
        }
        catch (Exception ex)
        {
            LogMessage($"Error checking for updates: forcing cache update - {ex.Message}");
            return true;
        }
    }

    private async Task<bool> DoesCacheNeedUpdate(string cachedPath, (JObject rtx, JObject normals) remoteManifests)
    {
        try
        {
            using var archive = ZipFile.OpenRead(cachedPath);

            async Task<JObject?> TryReadManifest(string partialPath)
            {
                var entry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith(partialPath, StringComparison.OrdinalIgnoreCase));
                if (entry == null) return null;

                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                return JObject.Parse(json);
            }

            var rtxManifest = await TryReadManifest("Vanilla-RTX/manifest.json");
            bool rtxIsNewer = rtxManifest != null && IsRemoteVersionNewer(rtxManifest, remoteManifests.rtx);

            var normalsManifest = await TryReadManifest("Vanilla-RTX-Normals/manifest.json");
            bool normalsIsNewer = normalsManifest != null && IsRemoteVersionNewer(normalsManifest, remoteManifests.normals);

            return rtxIsNewer || normalsIsNewer;


            return false;
        }
        catch
        {
            return true;
        }
    }

    private bool IsRemoteVersionNewer(JObject cachedManifest, JObject remoteManifest)
    {
        try
        {
            var cachedVersion = cachedManifest["header"]?["version"]?.ToObject<int[]>();
            var remoteVersion = remoteManifest["header"]?["version"]?.ToObject<int[]>();

            if (cachedVersion == null || remoteVersion == null) return true;

            for (int i = 0; i < Math.Max(cachedVersion.Length, remoteVersion.Length); i++)
            {
                int cached = i < cachedVersion.Length ? cachedVersion[i] : 0;
                int remote = i < remoteVersion.Length ? remoteVersion[i] : 0;

                if (remote > cached) return true;
                if (remote < cached) return false;
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    private async Task<(JObject rtx, JObject normals)?> FetchRemoteManifests()
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", $"vanilla_rtx_app_updater/{TunerVariables.appVersion} (https://github.com/Cubeir/Vanilla-RTX-App)");

            var rtxTask = client.GetStringAsync(VANILLA_RTX_MANIFEST_URL);
            var normalsTask = client.GetStringAsync(VANILLA_RTX_NORMALS_MANIFEST_URL);

            var rtxResponse = await rtxTask;
            var normalsResponse = await normalsTask;

            return (JObject.Parse(rtxResponse), JObject.Parse(normalsResponse));
        }
        catch
        {
            return null;
        }
    }

    private async Task<(bool Success, string? Path)> DownloadLatestPackage()
    {
        try
        {
            return await Helpers.Download(VANILLA_RTX_REPO_ZIPBALL_URL);
        }
        catch (Exception ex)
        {
            LogMessage($"Download error: {ex.Message}");
            return (false, null);
        }
    }


    // ---------- Deployment methods


    private async Task<bool> DeployPackage(string packagePath)
    {
        if (PackUpdater.IsMinecraftRunning() && RuntimeFlags.Set("Has_Told_User_To_Close_The_Game"))
        {
            LogMessage("⚠️ Minecraft is running. Please close the game while using the app.");
        }

        bool success_status = false;
        string tempExtractionDir = null;
        string resourcePackPath = null;

        try
        {
            // Find the resource pack path, where we wanna deploy
            string basePath = TunerVariables.Persistent.IsTargetingPreview
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Minecraft Bedrock Preview")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Minecraft Bedrock");

            if (!Directory.Exists(basePath))
            {
                LogMessage("❌ Minecraft data root not found. Please make sure the game is installed or has been launched at least once.");
                return false;
            }

            resourcePackPath = Path.Combine(basePath, "Users", "Shared", "games", "com.mojang", "resource_packs");

            if (!Directory.Exists(resourcePackPath))
            {
                Directory.CreateDirectory(resourcePackPath);
                LogMessage("📁 Shared resources directory was missing and has been created.");
            }

            // Step 1: Extract zipball directly into resource pack directory with UUID suffix
            tempExtractionDir = Path.Combine(resourcePackPath, $"_vrtxapptemp_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempExtractionDir);

            ZipFile.ExtractToDirectory(packagePath, tempExtractionDir, overwriteFiles: true);
            // LogMessage("📦 Extracted package to temporary directory");

            // Step 2: Find which packs exist in the zipball extraction
            var extractedManifests = Directory.GetFiles(tempExtractionDir, "manifest.json", SearchOption.AllDirectories);

            string vanillaRTXSrc = null;
            string vanillaRTXNormalsSrc = null;
            bool foundVanillaRTX = false;
            bool foundVanillaRTXNormals = false;

            foreach (var manifestPath in extractedManifests)
            {
                var uuids = await ReadManifestUUIDs(manifestPath);
                if (uuids == null) continue;

                var (headerUUID, moduleUUID) = uuids.Value;

                if (headerUUID == VANILLA_RTX_HEADER_UUID && moduleUUID == VANILLA_RTX_MODULE_UUID)
                {
                    vanillaRTXSrc = Path.GetDirectoryName(manifestPath);
                    foundVanillaRTX = true;
                }
                else if (headerUUID == VANILLA_RTX_NORMALS_HEADER_UUID && moduleUUID == VANILLA_RTX_NORMALS_MODULE_UUID)
                {
                    vanillaRTXNormalsSrc = Path.GetDirectoryName(manifestPath);
                    foundVanillaRTXNormals = true;
                }
            }

            if (!foundVanillaRTX && !foundVanillaRTXNormals)
            {
                LogMessage("❌ Neither Vanilla-RTX nor Vanilla-RTX-Normals were found in the downloaded package.");
                return false;
            }

            if (!foundVanillaRTX || !foundVanillaRTXNormals)
            {
                LogMessage("⚠️ Extracted zipball was missing one of the packs.");
            }

            // Step 3: For each pack found in zipball, delete existing versions from resource pack root
            ForceWritable(resourcePackPath);

            // Get all manifests in resource pack root, excluding CURRENT temp extraction directory
            var existingManifests = Directory.GetFiles(resourcePackPath, "manifest.json", SearchOption.AllDirectories)
                .Where(manifestPath => !manifestPath.StartsWith(tempExtractionDir, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // Delete existing Vanilla RTX if we found it in zipball
            if (foundVanillaRTX)
            {
                await DeleteExistingPackByUUID(existingManifests, resourcePackPath,
                    VANILLA_RTX_HEADER_UUID, VANILLA_RTX_MODULE_UUID, "Vanilla RTX");
            }

            // Delete existing Vanilla RTX Normals if we found it in zipball
            if (foundVanillaRTXNormals)
            {
                await DeleteExistingPackByUUID(existingManifests, resourcePackPath,
                    VANILLA_RTX_NORMALS_HEADER_UUID, VANILLA_RTX_NORMALS_MODULE_UUID, "Vanilla RTX Normals");
            }

            // Step 4: Atomically move and rename pack directories
            if (foundVanillaRTX && vanillaRTXSrc != null)
            {
                LogMessage("✅ Deploying Vanilla RTX.");

                var finalDestination = GetSafeDirectoryName(resourcePackPath, "vrtx");
                Directory.Move(vanillaRTXSrc, finalDestination);

                ProcessEnhancementFolders(finalDestination);
            }

            if (foundVanillaRTXNormals && vanillaRTXNormalsSrc != null)
            {
                LogMessage("✅ Deploying Vanilla RTX Normals.");

                var finalDestination = GetSafeDirectoryName(resourcePackPath, "vrtxn");
                Directory.Move(vanillaRTXNormalsSrc, finalDestination);

                ProcessEnhancementFolders(finalDestination);
            }



            success_status = true;
            return true;
        }
        catch (Exception ex)
        {
            LogMessage($"❌ Deployment error: {ex.Message}");
            return false;
        }
        finally
        {
            // Step 5: Clean up current temp extraction directory
            if (tempExtractionDir != null && Directory.Exists(tempExtractionDir))
            {
                try
                {
                    ForceWritable(tempExtractionDir);
                    Directory.Delete(tempExtractionDir, true);
                    LogMessage(success_status ? "✅ Deployment completed and cleaned up." : "🧹 Cleaned up after failed deployment.");
                }
                catch (Exception ex)
                {
                    LogMessage($"⚠️ Failed to clean up temp directory: {ex.Message}");
                }
            }

            // Step 6: Clean up any orphaned temp directories from previous runs
            if (resourcePackPath != null)
            {
                try
                {
                    var orphanedDirs = Directory.GetDirectories(resourcePackPath, "_vrtxapptemp_*", SearchOption.TopDirectoryOnly);
                    foreach (var orphanedDir in orphanedDirs)
                    {
                        try
                        {
                            ForceWritable(orphanedDir);
                            Directory.Delete(orphanedDir, true);
                            LogMessage($"🧹 Removed orphaned temp directories.");
                        }
                        catch
                        {
                            // Silently ignore - might be in use or permission issue
                        }
                    }
                }
                catch
                {
                    // Silently ignore orphaned cleanup failures - not critical
                }
            }
        }
    }
    private string GetSafeDirectoryName(string parentPath, string desiredName)
    {
        var fullPath = Path.Combine(parentPath, desiredName);

        // If directory doesn't exist, we can use it
        if (!Directory.Exists(fullPath))
            return fullPath;

        // If directory exists but is empty, we can safely overwrite
        if (Directory.GetFileSystemEntries(fullPath).Length == 0)
        {
            Directory.Delete(fullPath);
            return fullPath;
        }

        // Directory exists and has files, find a numbered suffix
        int suffix = 1;
        string safeName;
        do
        {
            safeName = Path.Combine(parentPath, $"{desiredName}{suffix}");
            suffix++;
        } while (Directory.Exists(safeName) && Directory.GetFileSystemEntries(safeName).Length > 0);

        // If we found an existing empty directory with this numbered name, delete it
        if (Directory.Exists(safeName))
            Directory.Delete(safeName);

        return safeName;
    }
    private async Task DeleteExistingPackByUUID(List<string> existingManifests, string resourcePackPath,
        string targetHeaderUUID, string targetModuleUUID, string packName)
    {
        foreach (var manifestPath in existingManifests)
        {
            var uuids = await ReadManifestUUIDs(manifestPath);
            if (uuids == null) continue;

            var (headerUUID, moduleUUID) = uuids.Value;

            if (headerUUID == targetHeaderUUID && moduleUUID == targetModuleUUID)
            {
                var topLevelFolder = GetTopLevelFolderForManifest(manifestPath, resourcePackPath);
                if (topLevelFolder != null && Directory.Exists(topLevelFolder))
                {
                    ForceWritable(topLevelFolder);
                    Directory.Delete(topLevelFolder, true);
                    LogMessage($"🗑️ Removed previous installation of: {packName}");
                }
            }
        }
    }
    private void ForceWritable(string path)
    {
        var di = new DirectoryInfo(path);
        if (!di.Exists) return;

        // Only modify if actually read-only
        if ((di.Attributes & System.IO.FileAttributes.ReadOnly) != 0)
            di.Attributes &= ~System.IO.FileAttributes.ReadOnly;

        foreach (var file in di.GetFiles("*", SearchOption.AllDirectories))
        {
            if ((file.Attributes & System.IO.FileAttributes.ReadOnly) != 0)
                file.Attributes &= ~System.IO.FileAttributes.ReadOnly;
        }

        foreach (var dir in di.GetDirectories("*", SearchOption.AllDirectories))
        {
            if ((dir.Attributes & System.IO.FileAttributes.ReadOnly) != 0)
                dir.Attributes &= ~System.IO.FileAttributes.ReadOnly;
        }
    }
    private async Task<(string headerUUID, string moduleUUID)?> ReadManifestUUIDs(string manifestPath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(manifestPath);
            var data = JObject.Parse(json);

            string headerUUID = data["header"]?["uuid"]?.ToString();
            string moduleUUID = data["modules"]?[0]?["uuid"]?.ToString();

            return (headerUUID, moduleUUID);
        }
        catch
        {
            return null;
        }
    }



    // ---------- Enhanced option enabler
    private void ProcessEnhancementFolders(string rootDirectory)
    {
        if (string.IsNullOrEmpty(EnhancementFolderName))
        {
            return; // Skip if no folder name is specified
        }

        // Find all directories with the specified name recursively
        var enhancementFolders = Directory.GetDirectories(rootDirectory, EnhancementFolderName, SearchOption.AllDirectories)
                                          .ToList();

        if (enhancementFolders.Count == 0)
        {
            LogMessage($"No '{EnhancementFolderName}' folders found.");
            return;
        }

        foreach (var enhancementPath in enhancementFolders)
        {
            try
            {
                // Get the parent directory
                string parentDirectory = Directory.GetParent(enhancementPath).FullName;

                LogMessage($"Processing {EnhancementFolderName} at: {enhancementPath}");

                // Copy contents
                CopyDirectoryContents(enhancementPath, parentDirectory);

                // Delete
                ForceWritable(enhancementPath);
                Directory.Delete(enhancementPath, true);

                LogMessage($"✅ Successfully processed and removed '{EnhancementFolderName}' folder.");
            }
            catch (Exception ex)
            {
                LogMessage($"⚠️ Error processing '{EnhancementFolderName}' folder at {enhancementPath}: {ex.Message}");
            }
        }
    }
    private void CopyDirectoryContents(string sourceDir, string destDir)
    {
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            string fileName = Path.GetFileName(file);
            string destFile = Path.Combine(destDir, fileName);
            File.Copy(file, destFile, true); // overwrite
            LogMessage($"  Copied file: {fileName}");
        }
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            string dirName = Path.GetFileName(subDir);
            string destSubDir = Path.Combine(destDir, dirName);
            Directory.CreateDirectory(destSubDir);
            CopyDirectoryContents(subDir, destSubDir);
        }
    }


    // ---------- Caching Helpers
    private (bool exists, string path) GetCacheInfo()
    {
        var localSettings = ApplicationData.Current.LocalSettings;
        var cachedPath = localSettings.Values["CachedZipballPath"] as string;
        bool exists = !string.IsNullOrEmpty(cachedPath) && System.IO.File.Exists(cachedPath);
        if (exists)
        {
            try
            {
                using (ZipFile.OpenRead(cachedPath)) { }
            }
            catch
            {
                LogMessage("⚠️ Cached package is corrupted, proceeding as if no cache was available.");
                exists = false;
                cachedPath = null;
            }
        }
        return (exists, cachedPath);
    }

    private void SaveCachedZipballPath(string path)
    {
        var localSettings = ApplicationData.Current.LocalSettings;
        localSettings.Values["CachedZipballPath"] = path;
    }

    public bool HasDeployableCache()
    {
        var (exists, _) = GetCacheInfo();
        return exists;
    }


    // ---------- Other Helpers
    private string GetTopLevelFolderForManifest(string manifestPath, string resourcePackPath)
    {
        var manifestDir = Path.GetDirectoryName(manifestPath);
        var resourcePackDir = new DirectoryInfo(resourcePackPath);
        var currentDir = new DirectoryInfo(manifestDir);

        // Walk up the directory tree until we find the direct child of resourcePackPath
        while (currentDir != null && currentDir.Parent != null)
        {
            if (currentDir.Parent.FullName.Equals(resourcePackDir.FullName, StringComparison.OrdinalIgnoreCase))
            {
                return currentDir.FullName;
            }
            currentDir = currentDir.Parent;
        }

        return null;
    }

    private void LogMessage(string message)
    {
        _logMessages.Add($"{message}");
        ProgressUpdate?.Invoke(message);
    }

    public static bool IsMinecraftRunning()
    {
        var mcProcesses = Process.GetProcessesByName("Minecraft.Windows");

        return mcProcesses.Length > 0;
    }
}

/// =====================================================================================================================
/// Silently tries to update the credits from Vanilla RTX's readme -- any failure will result in null.
/// Cooldowns also result in null, check for null and don't show credits whereever this class is used.
/// =====================================================================================================================

public class CreditsUpdater
{
    private const string CREDITS_CACHE_KEY = "CreditsCache";
    private const string CREDITS_TIMESTAMP_KEY = "CreditsTimestamp";
    private const string CREDITS_LAST_SHOWN_KEY = "CreditsLastShown";
    private const string README_URL = "https://raw.githubusercontent.com/Cubeir/Vanilla-RTX/master/README.md";
    private const int CACHE_UPDATE_COOLDOWN_DAYS = 2;
    private const int DISPLAY_COOLDOWN_DAYS = 0;

    public static string Credits { get; private set; } = string.Empty;
    private static readonly object _updateLock = new();
    private static bool _isUpdating = false;

    public static string GetCredits(bool returnString = false)
    {
        try
        {
            var updater = new CreditsUpdater();
            var cachedCredits = updater.GetCachedCredits();

            // If no cache or update cooldown expired, trigger background update (only one at a time)
            if ((string.IsNullOrEmpty(cachedCredits) || updater.ShouldUpdateCache()) && !_isUpdating)
            {
                lock (_updateLock)
                {
                    if (!_isUpdating) // Double-check inside lock
                    {
                        _isUpdating = true;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                // MainWindow.Instance?.BlinkingLamp(true);
                                var freshCredits = await updater.FetchAndExtractCreditsAsync();
                                if (!string.IsNullOrEmpty(freshCredits))
                                {
                                    Credits = freshCredits;
                                    updater.CacheCredits(freshCredits);
                                }
                            }
                            catch
                            {
                                // Silently fail background update
                            }
                            finally
                            {
                                // MainWindow.Instance?.BlinkingLamp(false);
                                lock (_updateLock)
                                {
                                    _isUpdating = false;
                                }
                            }
                        });
                    }
                }
            }

            // Check display cooldown - return null if still in cooldown period
            if (!updater.ShouldShowCredits())
            {
                return null;
            }

            // Update last shown timestamp when credits are about to be displayed
            if (!string.IsNullOrEmpty(cachedCredits))
            {
                updater.UpdateLastShownTimestamp();
            }

            // Only return credits if display is allowed AND cache exists
            return returnString && !string.IsNullOrEmpty(cachedCredits) ? cachedCredits : null;
        }
        catch
        {
            return null;
        }
    }

    private string? GetCachedCredits()
    {
        try
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            return localSettings.Values.TryGetValue(CREDITS_CACHE_KEY, out var value)
                ? value.ToString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private bool ShouldUpdateCache()
    {
        try
        {
            var localSettings = ApplicationData.Current.LocalSettings;

            if (!localSettings.Values.TryGetValue(CREDITS_TIMESTAMP_KEY, out var value))
                return true;

            if (DateTime.TryParse(value.ToString(), out DateTime cachedTime))
            {
                return DateTime.Now - cachedTime >= TimeSpan.FromDays(CACHE_UPDATE_COOLDOWN_DAYS);
            }

            return true;
        }
        catch
        {
            return true;
        }
    }

    private bool ShouldShowCredits()
    {
        try
        {
            var localSettings = ApplicationData.Current.LocalSettings;

            if (!localSettings.Values.TryGetValue(CREDITS_LAST_SHOWN_KEY, out var value))
                return true; // Never shown before, allow showing

            if (DateTime.TryParse(value.ToString(), out DateTime lastShownTime))
            {
                return DateTime.Now - lastShownTime >= TimeSpan.FromDays(DISPLAY_COOLDOWN_DAYS);
            }

            return true;
        }
        catch
        {
            return true;
        }
    }

    private void UpdateLastShownTimestamp()
    {
        try
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values[CREDITS_LAST_SHOWN_KEY] = DateTime.Now.ToString();
        }
        catch
        {
            // Silently ignore timestamp update failures
        }
    }

    private async Task<string> FetchAndExtractCreditsAsync()
    {
        try
        {
            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                string userAgent = $"vanilla_rtx_app_updater/{TunerVariables.appVersion} (https://github.com/Cubeir/Vanilla-RTX-App)";
                client.DefaultRequestHeaders.Add("User-Agent", userAgent);

                var response = await client.GetAsync(README_URL);
                if (!response.IsSuccessStatusCode)
                    return null;

                var content = await response.Content.ReadAsStringAsync();

                // Extract credits between "### Credits" and "——"
                int creditsIndex = content.IndexOf("### Credits", StringComparison.OrdinalIgnoreCase);
                if (creditsIndex == -1)
                    return null;

                string afterCredits = content.Substring(creditsIndex + "### Credits".Length).Trim();
                int delimiterIndex = afterCredits.IndexOf("——");
                if (delimiterIndex == -1)
                    return null;

                return afterCredits.Substring(0, delimiterIndex).Trim() +
                       "\n\nConsider supporting development of Vanilla RTX, maybe you'll find your name here next time!? ❤️";
            }
        }
        catch
        {
            return null;
        }
    }

    private void CacheCredits(string credits)
    {
        try
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values[CREDITS_CACHE_KEY] = credits;
            localSettings.Values[CREDITS_TIMESTAMP_KEY] = DateTime.Now.ToString();
        }
        catch
        {
            // Silent fails
        }
    }

    public static void ForceUpdateCache()
    {
        try
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values[CREDITS_TIMESTAMP_KEY] = DateTime.Now.AddDays(-10).ToString();
            localSettings.Values[CREDITS_LAST_SHOWN_KEY] = DateTime.Now.AddDays(-10).ToString();
        }
        catch
        {
        }
    }
}


/// =====================================================================================================================
/// Show PSA from github readme, simply add a ### PSA tag followed by the announcement at the end of the readme file linked below
/// =====================================================================================================================

public class PSAUpdater
{
    private const string README_URL = "https://raw.githubusercontent.com/Cubeir/Vanilla-RTX-App/master/README.md";
    private const string CACHE_KEY = "PSAContentCache";
    private const string TIMESTAMP_KEY = "PSALastCheckedTimestamp";
    private static readonly TimeSpan COOLDOWN = TimeSpan.FromHours(4);

    public static async Task<string?> GetPSAAsync()
    {
        try
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;

            // Check if we have cached data and if cooldown is still active
            if (localSettings.Values.ContainsKey(TIMESTAMP_KEY) &&
                localSettings.Values.ContainsKey(CACHE_KEY))
            {
                var lastChecked = DateTime.Parse(localSettings.Values[TIMESTAMP_KEY] as string);
                if (DateTime.UtcNow - lastChecked < COOLDOWN)
                {
                    // Return cached content
                    return localSettings.Values[CACHE_KEY] as string;
                }
            }

            // Cooldown expired or no cache, fetch new data
            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                var userAgent = $"vanilla_rtx_app_updater/{TunerVariables.appVersion} (https://github.com/Cubeir/Vanilla-RTX-App)";
                client.DefaultRequestHeaders.Add("User-Agent", userAgent);
                var response = await client.GetAsync(README_URL);
                if (!response.IsSuccessStatusCode)
                    return localSettings.Values[CACHE_KEY] as string; // Return cached on failure

                var content = await response.Content.ReadAsStringAsync();
                int psaIndex = content.IndexOf("### PSA", StringComparison.OrdinalIgnoreCase);
                if (psaIndex == -1)
                    return null;

                var afterPSA = content.Substring(psaIndex + "### PSA".Length).Trim();
                var result = string.IsNullOrWhiteSpace(afterPSA) ? null : afterPSA;

                // Cache the result and timestamp
                localSettings.Values[CACHE_KEY] = result;
                localSettings.Values[TIMESTAMP_KEY] = DateTime.UtcNow.ToString("O");

                return result;
            }
        }
        catch
        {
            // On error, return previously cached content if available
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            return localSettings.Values.ContainsKey(CACHE_KEY)
                ? localSettings.Values[CACHE_KEY] as string
                : null;
        }
    }
}