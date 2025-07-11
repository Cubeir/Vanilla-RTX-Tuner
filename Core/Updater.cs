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
using Vanilla_RTX_Tuner_WinUI;
using Vanilla_RTX_Tuner_WinUI.Core;
using Windows.Storage;
using static System.Net.WebRequestMethods;

public class AppUpdater
{
    public const string API_URL = "https://api.github.com/repos/Cubeir/Vanilla-RTX-Tuner/releases/latest";
    public static string latestAppVersion = null;
    public static string latestAppRemote_URL = null;

    public static async Task<(bool, string)> CheckGitHubForUpdates()
    {
        if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
            return (false, "No Internet Connection");

        try
        {
            MainWindow.PushLog("Checking GitHub for updates...");

            using (HttpClient client = new HttpClient())
            {
                string userAgent = $"vanilla_rtx_tuner_updater/{TunerVariables.appVersion}";
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
                    return (false, "No assets found in latest release");
                }

                // Find the target asset (Should always start with "Vanilla.RTX.Tuner.WinUI_[version]")
                var targetAsset = assets.FirstOrDefault(asset =>
                    asset["name"]?.ToString().StartsWith("Vanilla.RTX.Tuner.WinUI_") == true);

                if (targetAsset == null)
                {
                    return (false, "Target asset not found in release");
                }

                // Extract version and DL url from filename
                string fileName = targetAsset["name"].ToString();
                string downloadUrl = targetAsset["browser_download_url"].ToString();

                var versionMatch = Regex.Match(fileName, @"Vanilla\.RTX\.Tuner\.WinUI_(\d+\.\d+\.\d+\.\d+)\.zip");
                if (!versionMatch.Success)
                {
                    return (false, $"Could not extract version from filename: {fileName}");
                }

                string extractedVersion = versionMatch.Groups[1].Value;

                // Store the extracted data (if either fails, updating won't hapepn)
                latestAppVersion = extractedVersion;
                latestAppRemote_URL = downloadUrl;

                // MainWindow.PushLog($"Latest Version: {extractedVersion}");

                // See if we need updates
                if (IsVersionHigher(extractedVersion, TunerVariables.appVersion))
                {
                    return (true, $"A New App Version is Available! Latest: {extractedVersion} - Click again to begin download & installation.");
                }
                else
                {
                    return (false, "Current version is up to date.");
                }
            }
        }
        catch (Exception ex)
        {
            return (false, $"Error checking for updates: {ex.Message}");
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
        catch
        {
            MainWindow.PushLog("Error while comparing versions");
            return false;
        }
    }

    public static async Task<(bool, string)> InstallAppUpdate()
    {
        try
        {
            if (string.IsNullOrEmpty(latestAppRemote_URL))
            {
                return (false, "Error: No update URL available");
            }

            // Download the update package
            var downloadResult = await Helpers.Download(latestAppRemote_URL);

            if (!downloadResult.Item1 || string.IsNullOrEmpty(downloadResult.Item2))
            {
                return (false, "Failed to download update package properly.");
            }

            string zipFilePath = downloadResult.Item2;

            // Create unique extraction directory
            string extractionDir = Path.Combine(
                Path.GetDirectoryName(zipFilePath),
                $"Vanilla_RTX_Tuner_AutoUpdater_{Guid.NewGuid():N}"
            );

            // Extract the zip file
            ZipFile.ExtractToDirectory(zipFilePath, extractionDir);

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
                        return (true, "Installer started successfully, accept the UAC prompt.");
                    }
                    else
                    {
                        return (false, "Failed to start installer script.");
                    }
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    // TODO: could probably use this error and keep the updater in limbo, if update button is pressed yet again it tries to reopen this batch script.
                    // Probably not worth the spaghetti though
                    return (false, "Installation cancelled - Administrator privileges are required. Try updating again.");
                }
                catch (Exception ex)
                {
                    return (false, $"Error during update installation: {ex.Message}");
                }
            }
            else
            {
                // Search for .msix file if Installer.bat not found (just in case some dumbass anti-virus decides to wipe it upon extraction, msix is safe)
                // Besides that we assume user already installed the app via installer.bat at least once, and my self-signed certificate already lasts a good 10 years so...
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
                        return (true, "MSIX installer started successfully, continue to update in Windows App Installer. (Installer.bat was not found - possibly removed by antivirus)");
                    }
                    else
                    {
                        return (false, "Failed to start MSIX installer");
                    }
                }
                else
                {
                    return (false, "Neither Installer.bat nor .msix file found in update package");
                }
            }
        }
        catch (Exception ex)
        {
            return (false, $"Error during update installation: {ex.Message}");
        }
    }
}

/// 🧱🧱🧱🧱🧱🧱🧱🧱🧱🧱🧱🧱🧱🧱🧱🧱🧱🧱🧱🧱🧱🧱🧱🧱🧱

/// <summary>
/// Updates packs and returns a detailed log of what happeneded:
/*
 * Try to use cached zipball if available
 * Check cache against remote manifest
 * If remote manifest shows higher version than cache, update the cache before deploying. Otherwise deploy the cache normally.
 * 
 * But if the cache is missing or corrupt for whatever the reason, get the latest zipball and update cache, deploy.
 * If unable to get latest manifest from remote, just download latest and update cache anyway.
 * If unable to get latest zipball from remote, and we already have a cache, use the cache for deployment anyway.
 * 
 * If no cached zipball and no access to remote, that's when you can't reinstall -- so Reinstall button needs to run with internet at least once.
 * 
 * The code could absolutely be refactored, it works, but the design is incremental because I thought of it on the fly
 * A cleaner way to go about it would be something to first determine what to do in one go, and deploy from there
 * 
 * The logging copilot added is garbage, do something clearer
 * 
 * There are 7 execution paths with all edge cases
 * 
 * Chopped up the logic, it starts diverging based on existence of a Cache
 * If a cache exists, it checks if an update is needed, if can't update or update isn't needed, use the current one
 * If a cache doesn't exist, proceed as normal -- manifest version is only relevant when we have a cache to compare against
 * So the previous "decision maker" method based on the situation was just making it worse.
*/
/// </summary>
public class PackUpdater
{
    public const string VanillaRTX_Manifest_URL = "https://raw.githubusercontent.com/Cubeir/Vanilla-RTX/master/Vanilla-RTX/manifest.json";
    public const string VanillaRTXNormals_Manifest_URL = "https://raw.githubusercontent.com/Cubeir/Vanilla-RTX/master/Vanilla-RTX-Normals/manifest.json";
    public const string zipball_URL = "https://api.github.com/repos/Cubeir/Vanilla-RTX/zipball/master";

    private string vanillaRTXHeaderUUID => PackLocator.vanillaRTXHeaderUUID;
    private string vanillaRTXModuleUUID => PackLocator.vanillaRTXModuleUUID;
    private string vanillaRTXNormalsHeaderUUID => PackLocator.vanillaRTXNormalsHeaderUUID;
    private string vanillaRTXNormalsModuleUUID => PackLocator.vanillaRTXNormalsModuleUUID;

    // Event for progress updates to avoid UI thread blocking
    public event Action<string> ProgressUpdate;

    private readonly List<string> _logMessages = new List<string>();

    public async Task<(bool Success, List<string> Logs)> UpdatePacksAsync()
    {
        _logMessages.Clear();

        try
        {
            var cacheInfo = GetCacheInfo();
            if (!cacheInfo.exists)
            {
                return await HandleNoCacheScenario();
            }
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

    private async Task<(bool Success, List<string> Logs)> HandleNoCacheScenario()
    {
        LogMessage("No cache found - downloading latest version");

        var (downloadSuccess, downloadPath) = await DownloadLatestPackage();
        if (!downloadSuccess || string.IsNullOrEmpty(downloadPath))
        {
            LogMessage("Download failed and no cache available");
            return (false, new List<string>(_logMessages));
        }

        SaveCachedZipballPath(downloadPath);
        LogMessage("Package downloaded and cached");

        var deploySuccess = await DeployPackage(downloadPath);
        LogMessage(deploySuccess ? "Deployment successful" : "Deployment failed");

        return (deploySuccess, new List<string>(_logMessages));
    }

    private async Task<(bool Success, List<string> Logs)> HandleCacheExistsScenario(string cachePath)
    {
        LogMessage("Cache found - checking for updates");

        var needsUpdate = await CheckIfUpdateNeeded(cachePath);

        if (needsUpdate)
        {
            LogMessage("Update needed - downloading latest version");
            var (downloadSuccess, downloadPath) = await DownloadLatestPackage();

            if (downloadSuccess && !string.IsNullOrEmpty(downloadPath))
            {
                SaveCachedZipballPath(downloadPath);
                LogMessage("New version downloaded and cached");

                var deploySuccess = await DeployPackage(downloadPath);
                LogMessage(deploySuccess ? "Update deployment successful" : "Update deployment failed");
                return (deploySuccess, new List<string>(_logMessages));
            }
            else
            {
                LogMessage("Download failed - falling back to cached version");
                var deploySuccess = await DeployPackage(cachePath);
                LogMessage(deploySuccess ? "Cached version deployed successfully" : "Cached version deployment failed");
                return (deploySuccess, new List<string>(_logMessages));
            }
        }
        else
        {
            LogMessage("Cache is up to date - deploying cached version");
            var deploySuccess = await DeployPackage(cachePath);
            LogMessage(deploySuccess ? "Cached version deployed successfully" : "Cached version deployment failed");
            return (deploySuccess, new List<string>(_logMessages));
        }
    }

    private async Task<bool> CheckIfUpdateNeeded(string cachePath)
    {
        if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
        {
            LogMessage("No network available - using cached version");
            return false;
        }

        try
        {
            var remoteManifests = await FetchRemoteManifests();
            if (remoteManifests == null)
            {
                LogMessage("Cannot fetch remote manifests - assuming update needed");
                return true;
            }

            var cacheNeedsUpdate = await DoesCacheNeedUpdate(cachePath, remoteManifests.Value);
            LogMessage(cacheNeedsUpdate ? "Remote version is newer" : "Cache is current");
            return cacheNeedsUpdate;
        }
        catch (Exception ex)
        {
            LogMessage($"Error checking for updates: {ex.Message} - assuming update needed");
            return true;
        }
    }

    private async Task<(JObject rtx, JObject normals)?> FetchRemoteManifests()
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", $"vanilla_rtx_tuner_updater/{TunerVariables.appVersion}");

            var rtxTask = client.GetStringAsync(VanillaRTX_Manifest_URL);
            var normalsTask = client.GetStringAsync(VanillaRTXNormals_Manifest_URL);

            var rtxResponse = await rtxTask;
            var normalsResponse = await normalsTask;

            return (JObject.Parse(rtxResponse), JObject.Parse(normalsResponse));
        }
        catch
        {
            return null;
        }
    }

    private async Task<(bool Success, string Path)> DownloadLatestPackage()
    {
        try
        {
            return await Helpers.Download(zipball_URL);
        }
        catch (Exception ex)
        {
            LogMessage($"Download error: {ex.Message}");
            return (false, null);
        }
    }

    private (bool exists, string path) GetCacheInfo()
    {
        var cachedPath = GetCachedZipballPath();
        var exists = !string.IsNullOrEmpty(cachedPath) && System.IO.File.Exists(cachedPath);
        return (exists, cachedPath);
    }

    private async Task<bool> DoesCacheNeedUpdate(string cachedPath, (JObject rtx, JObject normals) remoteManifests)
    {
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "rtx_version_check", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                ZipFile.ExtractToDirectory(cachedPath, tempDir);
                var rootDir = Directory.GetDirectories(tempDir).FirstOrDefault();
                if (rootDir == null) return true;

                // Check Vanilla RTX version
                var rtxManifestPath = Path.Combine(rootDir, "Vanilla-RTX", "manifest.json");
                if (System.IO.File.Exists(rtxManifestPath))
                {
                    var cachedManifest = JObject.Parse(await System.IO.File.ReadAllTextAsync(rtxManifestPath));
                    if (IsRemoteVersionNewer(cachedManifest, remoteManifests.rtx))
                        return true;
                }

                // Check Vanilla RTX Normals version
                var normalsManifestPath = Path.Combine(rootDir, "Vanilla-RTX-Normals", "manifest.json");
                if (System.IO.File.Exists(normalsManifestPath))
                {
                    var cachedManifest = JObject.Parse(await System.IO.File.ReadAllTextAsync(normalsManifestPath));
                    if (IsRemoteVersionNewer(cachedManifest, remoteManifests.normals))
                        return true;
                }

                return false;
            }
            finally
            {
                try
                {
                    Directory.Delete(tempDir, true);
                }
                catch { }
            }
        }
        catch
        {
            return true; // If we can't check, assume we need update
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

    private async Task<bool> DeployPackage(string packagePath)
    {
        try
        {
            LogMessage("Starting deployment...");

            var saveDir = Path.GetDirectoryName(packagePath);
            var stagingDir = Path.Combine(saveDir, "_staging", Guid.NewGuid().ToString());
            var extractDir = Path.Combine(stagingDir, "extracted");

            Directory.CreateDirectory(extractDir);

            try
            {
                LogMessage("Extracting package...");
                ZipFile.ExtractToDirectory(packagePath, extractDir, overwriteFiles: true);

                var resourcePackPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Packages",
                    TunerVariables.IsTargetingPreview ? "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe" : "Microsoft.MinecraftUWP_8wekyb3d8bbwe",
                    "LocalState", "games", "com.mojang", "resource_packs");

                if (!Directory.Exists(resourcePackPath))
                {
                    LogMessage("Resource pack directory not found");
                    return false;
                }

                LogMessage("Preparing resource pack directory...");
                ForceWritable(resourcePackPath);
                await RemoveExistingPacks(resourcePackPath);

                var vanillaRoot = Directory.GetDirectories(extractDir).FirstOrDefault();
                if (vanillaRoot == null)
                {
                    LogMessage("No content found in package");
                    return false;
                }

                LogMessage("Copying pack files...");
                CopyPackFolder(vanillaRoot, resourcePackPath, "Vanilla-RTX");
                CopyPackFolder(vanillaRoot, resourcePackPath, "Vanilla-RTX-Normals");

                LogMessage("Deployment completed");
                return true;
            }
            finally
            {
                CleanupStaging(stagingDir);
            }
        }
        catch (Exception ex)
        {
            LogMessage($"Deployment error: {ex.Message}");
            return false;
        }
    }

    private async Task RemoveExistingPacks(string resourcePackPath)
    {
        var manifestFiles = Directory.GetFiles(resourcePackPath, "manifest.json", SearchOption.AllDirectories);

        foreach (var file in manifestFiles)
        {
            try
            {
                var json = await System.IO.File.ReadAllTextAsync(file);
                var data = JObject.Parse(json);

                string headerUUID = data["header"]?["uuid"]?.ToString();
                string moduleUUID = data["modules"]?[0]?["uuid"]?.ToString();

                bool isOurPack = (headerUUID == vanillaRTXHeaderUUID && moduleUUID == vanillaRTXModuleUUID) ||
                                (headerUUID == vanillaRTXNormalsHeaderUUID && moduleUUID == vanillaRTXNormalsModuleUUID);

                if (isOurPack)
                {
                    var folder = Path.GetDirectoryName(file);
                    ForceWritable(folder);
                    Directory.Delete(folder, true);
                }
            }
            catch { }
        }
    }

    private void CopyPackFolder(string vanillaRoot, string resourcePackPath, string folderName)
    {
        var src = Path.Combine(vanillaRoot, folderName);
        if (!Directory.Exists(src)) return;

        var baseName = folderName.Replace("-", "");
        var dst = Path.Combine(resourcePackPath, baseName);
        var suffix = 1;

        while (Directory.Exists(dst))
        {
            var manifest = Path.Combine(dst, "manifest.json");
            if (System.IO.File.Exists(manifest))
            {
                try
                {
                    var json = System.IO.File.ReadAllText(manifest);
                    var data = JObject.Parse(json);

                    string headerUUID = data["header"]?["uuid"]?.ToString();
                    string moduleUUID = data["modules"]?[0]?["uuid"]?.ToString();

                    bool isOurPack = (headerUUID == vanillaRTXHeaderUUID && moduleUUID == vanillaRTXModuleUUID) ||
                                    (headerUUID == vanillaRTXNormalsHeaderUUID && moduleUUID == vanillaRTXNormalsModuleUUID);

                    if (isOurPack)
                    {
                        ForceWritable(dst);
                        Directory.Delete(dst, true);
                        break;
                    }
                }
                catch { }
            }

            dst = Path.Combine(resourcePackPath, $"{baseName}({suffix++})");
        }

        DirectoryCopy(src, dst, true);
    }

    private void ForceWritable(string path)
    {
        var di = new DirectoryInfo(path);
        if (!di.Exists) return;

        di.Attributes &= ~System.IO.FileAttributes.ReadOnly;
        foreach (var file in di.GetFiles("*", SearchOption.AllDirectories))
            file.Attributes &= ~System.IO.FileAttributes.ReadOnly;
        foreach (var dir in di.GetDirectories("*", SearchOption.AllDirectories))
            dir.Attributes &= ~System.IO.FileAttributes.ReadOnly;
    }

    private void DirectoryCopy(string sourceDir, string destDir, bool copySubDirs)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists)
            throw new DirectoryNotFoundException($"Source not found: {sourceDir}");

        Directory.CreateDirectory(destDir);

        foreach (var file in dir.GetFiles())
            file.CopyTo(Path.Combine(destDir, file.Name), true);

        if (copySubDirs)
        {
            foreach (var subdir in dir.GetDirectories())
            {
                var dst = Path.Combine(destDir, subdir.Name);
                DirectoryCopy(subdir.FullName, dst, true);
            }
        }
    }

    private void CleanupStaging(string stagingDir)
    {
        try
        {
            if (Directory.Exists(stagingDir))
            {
                ForceWritable(stagingDir);
                Directory.Delete(stagingDir, true);
            }
        }
        catch { }
    }

    private string GetCachedZipballPath()
    {
        var localSettings = ApplicationData.Current.LocalSettings;
        return localSettings.Values["CachedZipballPath"] as string;
    }

    private void SaveCachedZipballPath(string path)
    {
        var localSettings = ApplicationData.Current.LocalSettings;
        localSettings.Values["CachedZipballPath"] = path;
    }

    private void LogMessage(string message)
    {
        _logMessages.Add($"{message}");
        ProgressUpdate?.Invoke(message);
    }
}