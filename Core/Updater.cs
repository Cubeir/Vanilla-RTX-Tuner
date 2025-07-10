using System;
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

public class Updater
{
    public const string API_URL = "https://api.github.com/repos/Cubeir/Vanilla-RTX-Tuner/releases/latest";
    public static string latestAppVersion = null;
    public static string latestAppRemote_URL = null;

    // make a JANITOR method, tries to cleanup after tuner, by checking all potential locations for speicifc folder names, e.g. "Vanilla_RTX_Tuner_AutoUpdater_"
    // Basically, janitor looks in any possible DOWNLOAD path, and looks for a string input for folder name, and nukes that folder

    // It's very nice and cleanly done. TEST.
    // THEN do VANILLA RTX UPDATES the same manner, reinstall packages via API, better cleaning, and such.


    #region ------- App Updater 
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
                    return (true, $"A New Version is Available! Latest: {extractedVersion} - Click again to begin download & installation.");
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
    #endregion App Updater ------- 



    #region ------- Pack Updater 


    #endregion Pack Updater -------
}