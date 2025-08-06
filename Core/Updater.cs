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
using Vanilla_RTX_Tuner_WinUI.Core;
using Windows.Storage;
using static Vanilla_RTX_Tuner_WinUI.Core.PackLocator;

namespace Vanilla_RTX_Tuner_WinUI;

// Updating logging of classes here to allow it to properly use the actual Logging method with logLevels


/// =====================================================================================================================
/// Checks for updates by querying the GitHub API for the latest release of "Vanilla.RTX.Tuner.WinUI_"
/// comparing it to the current app version, and caching the update package locally to avoid redundant downloads in case user says no to the installation UAC admin prompt
/// If the cache becomes outdated or invalid, old cache is claered, downloads the latest update, and extracts it for installation. 
/// The InstallAppUpdate method uses the cached or freshly downloaded package to launch an installer (preferring Installer.bat or falling back to an .msix file)
/// =====================================================================================================================

public class AppUpdater
{
    public static string? latestAppVersion = null;
    public static string? latestAppRemote_URL = null;
    private const string API_URL = "https://api.github.com/repos/Cubeir/Vanilla-RTX-Tuner/releases/latest";

    private const string CACHE_APP_VERSION_KEY = "CachedAppUpdateVersion";
    private const string CACHE_APP_ZIP_PATH_KEY = "CachedAppUpdateZipPath";
    private const string CACHE_APP_EXTRACTION_PATH_KEY = "CachedAppUpdateExtractionPath";

    public static async Task<(bool, string)> CheckGitHubForUpdates()
    {
        var localSettings = ApplicationData.Current.LocalSettings;
        const string LastAppUpdateCheckKey = "LastAppUpdateCheckTime";
        var now = DateTimeOffset.UtcNow;

        if (localSettings.Values[LastAppUpdateCheckKey] is string lastCheckStr &&
            DateTimeOffset.TryParse(lastCheckStr, out var lastCheck))
        {
            var cooldownEnd = lastCheck.AddMinutes(0.15);
            if (now < cooldownEnd)
            {
                var secondsLeft = (int)Math.Ceiling((cooldownEnd - now).TotalSeconds);
                return (false, $"⏳ Wait {secondsLeft}s before checking for app update again.");
            }
        }

        // Save the check time before making the request
        localSettings.Values[LastAppUpdateCheckKey] = now.ToString("o");

        if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
            return (false, "🛜 No Connection Found.");

        try
        {
            MainWindow.Log("Checking for app updates.", MainWindow.LogLevel.Network);

            using (HttpClient client = new HttpClient())
            {
                string userAgent = $"vanilla_rtx_tuner_updater/{TunerVariables.appVersion} (https://github.com/Cubeir/Vanilla-RTX-Tuner)";
                client.DefaultRequestHeaders.Add("User-Agent", userAgent);

                // Get the latest release info json
                HttpResponseMessage response = await client.GetAsync(API_URL);
                response.EnsureSuccessStatusCode();

                string responseBody = await response.Content.ReadAsStringAsync();
                JObject release = JObject.Parse(responseBody);

                // See the URL if you find it confusing what we're looking for, there are only two we need.
                JArray assets = (JArray)release["assets"];
                if (assets == null || !assets.Any())
                {
                    return (false, "⚠️ No assets found in latest release.");
                }

                // Find the target asset (Should always start with "Vanilla.RTX.Tuner.WinUI_[version]")
                var targetAsset = assets.FirstOrDefault(asset =>
                    asset["name"]?.ToString().StartsWith("Vanilla.RTX.Tuner.WinUI_") == true);

                if (targetAsset == null)
                {
                    return (false, "⚠️ Target asset not found in release");
                }

                // Extract version and DL url from filename
                string fileName = targetAsset["name"].ToString();
                string downloadUrl = targetAsset["browser_download_url"].ToString();

                var versionMatch = Regex.Match(fileName, @"Vanilla\.RTX\.Tuner\.WinUI_(\d+(\.\d+){1,3})\.zip");
                if (!versionMatch.Success)
                {
                    return (false, $"⚠️ Could not extract version from filename: {fileName}");
                }

                string extractedVersion = versionMatch.Groups[1].Value;

                // See if we need updates
                if (IsVersionHigher(extractedVersion, TunerVariables.appVersion))
                {
                    latestAppVersion = extractedVersion;
                    latestAppRemote_URL = downloadUrl;

                    // Check if we have this version cached
                    if (IsCachedVersionValid(extractedVersion))
                    {
                        return (true, $"📦 A New App Version is Available\nLatest: {extractedVersion} - Click again to install from cache.");
                    }
                    else
                    {
                        return (true, $"📦 A New App Version is Available\nLatest: {extractedVersion} - Click again to begin download & installation.");
                    }
                }
                else
                {
                    return (false, "ℹ️ Current version is the latest.");
                }
            }
        }
        catch (Exception ex)
        {
            return (false, $"❌ Error checking for updates: {ex.Message}");
        }
    }

    private static bool IsCachedVersionValid(string targetVersion)
    {
        try
        {
            var localSettings = ApplicationData.Current.LocalSettings;

            // Check if we have cache data for this version
            if (!(localSettings.Values[CACHE_APP_VERSION_KEY] is string cachedVersion) ||
                cachedVersion != targetVersion)
            {
                return false;
            }

            if (!(localSettings.Values[CACHE_APP_ZIP_PATH_KEY] is string cachedZipPath) ||
                string.IsNullOrEmpty(cachedZipPath))
            {
                return false;
            }

            // Verify the cached zip file still exists and is valid
            if (!File.Exists(cachedZipPath))
            {
                return false;
            }

            // Basic validation: check if it's a valid zip file
            try
            {
                using (var archive = ZipFile.OpenRead(cachedZipPath))
                {
                    // Just check if we can read the zip - if this throws, it's corrupt
                    _ = archive.Entries.Count;
                }
            }
            catch
            {
                return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ClearCacheData()
    {
        try
        {
            var localSettings = ApplicationData.Current.LocalSettings;

            // Clear cache keys
            localSettings.Values.Remove(CACHE_APP_VERSION_KEY);
            localSettings.Values.Remove(CACHE_APP_ZIP_PATH_KEY);
            localSettings.Values.Remove(CACHE_APP_EXTRACTION_PATH_KEY);
        }
        catch (Exception ex)
        {
            MainWindow.Log($"Warning: Failed to clear cache data: {ex.Message}", MainWindow.LogLevel.Warning);
        }
    }

    private static void CleanupOldCacheFiles()
    {
        try
        {
            var localSettings = ApplicationData.Current.LocalSettings;

            // Get current cache paths to protect them
            string currentZipPath = localSettings.Values[CACHE_APP_ZIP_PATH_KEY] as string;
            string currentExtractionPath = localSettings.Values[CACHE_APP_EXTRACTION_PATH_KEY] as string;

            if (string.IsNullOrEmpty(currentZipPath) || !File.Exists(currentZipPath))
            {
                return; // No current cache to base cleanup on
            }

            string baseDirectory = Path.GetDirectoryName(currentZipPath);
            if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory))
            {
                return;
            }

            // Clean up old zip files (but not the current cached one)
            var zipFiles = Directory.GetFiles(baseDirectory, "Vanilla.RTX.Tuner.WinUI_*.zip");
            foreach (string zipFile in zipFiles)
            {
                if (!string.Equals(zipFile, currentZipPath, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        File.Delete(zipFile);
                        MainWindow.Log($"Cleaned up old zip: {Path.GetFileName(zipFile)}", MainWindow.LogLevel.Informational);
                    }
                    catch (Exception ex)
                    {
                        MainWindow.Log($"Warning: Could not delete old zip {zipFile}: {ex.Message}", MainWindow.LogLevel.Warning);
                    }
                }
            }

            // Clean up old extraction directories (but not the current one)
            var extractionDirs = Directory.GetDirectories(baseDirectory, "Vanilla_RTX_Tuner_AutoUpdater_*");
            foreach (string extractionDir in extractionDirs)
            {
                if (!string.Equals(extractionDir, currentExtractionPath, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        Directory.Delete(extractionDir, true);
                        MainWindow.Log($"Cleaned up old extraction dir: {Path.GetFileName(extractionDir)}", MainWindow.LogLevel.Informational);
                    }
                    catch (Exception ex)
                    {
                        MainWindow.Log($"Warning: Could not delete old extraction dir {extractionDir}: {ex.Message}", MainWindow.LogLevel.Warning);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            MainWindow.Log($"Warning: Failed to cleanup old cache files: {ex.Message}", MainWindow.LogLevel.Warning);
        }
    }

    private static void SaveCacheData(string version, string zipPath, string extractionPath)
    {
        try
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values[CACHE_APP_VERSION_KEY] = version;
            localSettings.Values[CACHE_APP_ZIP_PATH_KEY] = zipPath;
            localSettings.Values[CACHE_APP_EXTRACTION_PATH_KEY] = extractionPath;
        }
        catch (Exception ex)
        {
            MainWindow.Log($"Warning: Failed to save cache data: {ex.Message}", MainWindow.LogLevel.Warning);
        }
    }

    private static bool IsVersionHigher(string newVersion, string currentVersion)
    {
        try
        {
            var newParts = newVersion.Split('.').Select(int.Parse).ToArray();
            var currentParts = currentVersion.Split('.').Select(int.Parse).ToArray();

            for (int i = 0; i < Math.Max(newParts.Length, currentParts.Length); i++)
            {
                int newPart = i < newParts.Length ? newParts[i] : 0;
                int currentPart = i < currentParts.Length ? currentParts[i] : 0;

                if (newPart > currentPart) return true;
                if (newPart < currentPart) return false;
            }

            return false;
        }
        catch (Exception ex)
        {
            MainWindow.Log($"Version compare failed ❗ {ex.Message} | new: {newVersion}, current: {currentVersion}", MainWindow.LogLevel.Error);
            return false;
        }
    }

    public static async Task<(bool, string)> InstallAppUpdate()
    {
        try
        {
            if (string.IsNullOrEmpty(latestAppRemote_URL) || string.IsNullOrEmpty(latestAppVersion))
            {
                return (false, "Error: No update URL or version available");
            }

            // Clean up old cache files first, preserves the current one
            CleanupOldCacheFiles();

            string zipFilePath = null;
            string extractionDir = null;
            bool usedCache = false;

            // Try to use cached version first
            if (IsCachedVersionValid(latestAppVersion))
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                zipFilePath = localSettings.Values[CACHE_APP_ZIP_PATH_KEY] as string;

                // Check if extraction directory from cache still exists
                if (localSettings.Values[CACHE_APP_EXTRACTION_PATH_KEY] is string cachedExtractionPath &&
                    Directory.Exists(cachedExtractionPath))
                {
                    // Re-use existing extraction directory
                    extractionDir = cachedExtractionPath;
                    MainWindow.Log("Using cached extraction directory.", MainWindow.LogLevel.Informational);
                }
                else
                {
                    // Create new extraction directory and re-extract
                    extractionDir = Path.Combine(
                        Path.GetDirectoryName(zipFilePath),
                        $"Vanilla_RTX_Tuner_AutoUpdater_{Guid.NewGuid():N}"
                    );

                    // Extract the zip file
                    ZipFile.ExtractToDirectory(zipFilePath, extractionDir, true);

                    // Update cache with new extraction path
                    localSettings.Values[CACHE_APP_EXTRACTION_PATH_KEY] = extractionDir;
                    MainWindow.Log("Re-extracted cached zip to new directory.", MainWindow.LogLevel.Debug);
                }

                usedCache = true;
                MainWindow.Log("Using cached update package.", MainWindow.LogLevel.Informational);
            }
            else
            {
                // Clear any stale cache data
                ClearCacheData();

                // Download the update package
                var downloadResult = await Helpers.Download(latestAppRemote_URL);

                if (!downloadResult.Item1 || string.IsNullOrEmpty(downloadResult.Item2))
                {
                    return (false, "❌ Failed to download update package properly.");
                }

                zipFilePath = downloadResult.Item2;

                // Create unique extraction directory
                extractionDir = Path.Combine(
                    Path.GetDirectoryName(zipFilePath),
                    $"Vanilla_RTX_Tuner_AutoUpdater_{Guid.NewGuid():N}"
                );

                // Extract the zip file
                ZipFile.ExtractToDirectory(zipFilePath, extractionDir, true);

                // Save cache data before attempting installation
                SaveCacheData(latestAppVersion, zipFilePath, extractionDir);

                MainWindow.Log("Downloaded fresh update package.", MainWindow.LogLevel.Lengthy);
            }

            // Search for Installer.bat in the extraction directory
            string[] batFiles = Directory.GetFiles(extractionDir, "Installer.bat", SearchOption.AllDirectories);

            if (batFiles.Length > 0)
            {
                // Run the installer batch file
                string installerPath = batFiles[0];

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    WorkingDirectory = Path.GetDirectoryName(installerPath),
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    Verb = "runas"
                };

                try
                {
                    Process installerProcess = Process.Start(startInfo);

                    if (installerProcess != null)
                    {
                        // Installation started successfully - clear cache as it's no longer needed
                        ClearCacheData();
                        return (true, "Installer started successfully, accept the UAC prompt ℹ️");
                    }
                    else
                    {
                        return (false, "Failed to start installer script as Admin ❗");
                    }
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    // User cancelled UAC - keep cache data for next attempt
                    return (false, "Installation cancelled ❗ Administrator privileges are required. Try updating again.\n");
                }
                catch (Exception ex)
                {
                    return (false, $"Error during update installation: {ex.Message}");
                }
            }
            else
            {
                // Search for .msix file if Installer.bat not found
                string[] msixFiles = Directory.GetFiles(extractionDir, "*.msix", SearchOption.AllDirectories);

                if (msixFiles.Length > 0)
                {
                    // Run the MSIX file directly
                    string msixPath = msixFiles[0];

                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = msixPath,
                        UseShellExecute = true,
                        CreateNoWindow = false
                    };

                    Process msixProcess = Process.Start(startInfo);

                    if (msixProcess != null)
                    {
                        // Installation started successfully - clear cache as it's no longer needed
                        ClearCacheData();
                        return (true, "MSIX installer started successfully, continue to update in Windows App Installer ❗ (Installer.bat was not found, possibly removed by antivirus)");
                    }
                    else
                    {
                        return (false, "Failed to start MSIX installer ❗");
                    }
                }
                else
                {
                    return (false, "Neither Installer.bat nor .msix file found in update package. Please report this error.");
                }
            }
        }
        catch (Exception ex)
        {
            return (false, $"Error during update installation: {ex.Message}");
        }
    }
}

/// =====================================================================================================================
/// Only deals with cache, we don't care if user has Vanilla RTX installed or not, we compare versions of cache to remote
/// No cache? download latest, cache outdated? download latest, if there's a cache and the rest fails for whatever the reason, fallback to cache
/// Deployment deletes any pack that matches UUIDs as defined at the begenning of PackLocator class
/// =====================================================================================================================

public class PackUpdater
{
    private const string VANILLA_RTX_MANIFEST_URL = "https://raw.githubusercontent.com/Cubeir/Vanilla-RTX/master/Vanilla-RTX/manifest.json";
    private const string VANILLA_RTX_NORMALS_MANIFEST_URL = "https://raw.githubusercontent.com/Cubeir/Vanilla-RTX/master/Vanilla-RTX-Normals/manifest.json";
    private const string VANILLA_RTX_REPO_ZIPBALL_URL = "https://github.com/Cubeir/Vanilla-RTX/archive/refs/heads/master.zip";

    // Event for progress updates to avoid UI thread blocking
    public event Action<string>? ProgressUpdate;
    private readonly List<string> _logMessages = new List<string>();

    // For cooldown of checking for update to avoid spamming github
    private const string LastUpdateCheckKey = "LastPackUpdateCheckTime";
    private static readonly TimeSpan UpdateCooldown = TimeSpan.FromMinutes(3);

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
                LogMessage($"Skipped update check ⏳\nCooldown ends in: {minutesLeft} minute{(minutesLeft == 1 ? "" : "s")}");
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
            client.DefaultRequestHeaders.Add("User-Agent", $"vanilla_rtx_tuner_updater/{TunerVariables.appVersion} (https://github.com/Cubeir/Vanilla-RTX-Tuner)");

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
        if (PackUpdater.IsMinecraftRunning() && RanOnceFlag.Set("Has_Told_User_To_Close_The_Game"))
        {
            LogMessage("⚠️ Minecraft is running. Please close the game while using Tuner.");
        }

        bool success_status = false;
        string tempExtractionDir = null;
        string resourcePackPath = null;

        try
        {
            // Find the resource pack path, where we wanna deploy
            var packagesRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            string mcFolderPattern = TunerVariables.IsTargetingPreview
                ? "Microsoft.MinecraftWindowsBeta_"
                : "Microsoft.MinecraftUWP_";

            var mcRoot = Directory.GetDirectories(packagesRoot, mcFolderPattern + "*").FirstOrDefault();
            if (mcRoot == null)
            {
                LogMessage("Minecraft data root not found. Please make sure the game is installed or has been launched at least once ❗");
                return false;
            }

            resourcePackPath = Path.Combine(mcRoot, "LocalState", "games", "com.mojang", "resource_packs");
            if (!Directory.Exists(resourcePackPath))
            {
                Directory.CreateDirectory(resourcePackPath);
                LogMessage("Resource pack directory was missing and has been created ℹ️");
            }

            // Step 1: Extract zipball directly into resource pack directory with UUID suffix
            tempExtractionDir = Path.Combine(resourcePackPath, $"_tunertemp_{Guid.NewGuid()}");
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
                var tempDestination = GetSafeDirectoryName(resourcePackPath, Path.GetFileName(vanillaRTXSrc));
                Directory.Move(vanillaRTXSrc, tempDestination);

                var finalDestination = GetSafeDirectoryName(resourcePackPath, "VanillaRTX");
                Directory.Move(tempDestination, finalDestination);
            }

            if (foundVanillaRTXNormals && vanillaRTXNormalsSrc != null)
            {
                LogMessage("✅ Deploying Vanilla RTX Normals.");
                var tempDestination = GetSafeDirectoryName(resourcePackPath, Path.GetFileName(vanillaRTXNormalsSrc));
                Directory.Move(vanillaRTXNormalsSrc, tempDestination);

                var finalDestination = GetSafeDirectoryName(resourcePackPath, "VanillaRTXNormals");
                Directory.Move(tempDestination, finalDestination);
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
                    var orphanedDirs = Directory.GetDirectories(resourcePackPath, "_tunertemp_*", SearchOption.TopDirectoryOnly);
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
                    LogMessage($"🗑️ Removed previous {packName} pack.");
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
                LogMessage("Cached package is corrupted, treating as no cache available.");
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
    private const int CACHE_UPDATE_COOLDOWN_DAYS = 1;
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
                string userAgent = $"vanilla_rtx_tuner_updater/{TunerVariables.appVersion} (https://github.com/Cubeir/Vanilla-RTX-Tuner)";
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
                       "\n\nConsider supporting development of Vanilla RTX, maybe you'll find your name here next time?! ❤️";
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