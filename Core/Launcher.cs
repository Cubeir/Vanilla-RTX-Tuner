using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Vanilla_RTX_Tuner_WinUI.Core;
public class Launcher
{
    public static async Task<string> LaunchMinecraftRTXAsync(bool isTargetingPreview)
    {
        try
        {
            string optionsFilePath, protocol, versionName;

            if (isTargetingPreview)
            {
                optionsFilePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Packages", "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe",
                    "LocalState", "games", "com.mojang", "minecraftpe", "options.txt"
                );
                protocol = "minecraft-preview://";
                versionName = "Minecraft Preview";
            }
            else
            {
                optionsFilePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Packages", "Microsoft.MinecraftUWP_8wekyb3d8bbwe",
                    "LocalState", "games", "com.mojang", "minecraftpe", "options.txt"
                );
                protocol = "minecraft://";
                versionName = "Minecraft";
            }

            if (string.IsNullOrEmpty(optionsFilePath))
            {
                return "❗ Failed to construct options file path.";
            }

            if (!File.Exists(optionsFilePath))
            {
                return $"Options file for {versionName} not found at: {optionsFilePath}\n" +
                       "❗ Make sure the correct version of the game is installed and has been launched at least once.";
            }

            // Check file accessibility
            try
            {
                using (var fileStream = File.Open(optionsFilePath, FileMode.Open, FileAccess.ReadWrite))
                {
                    // file is accessible
                }
            }
            catch (UnauthorizedAccessException)
            {
                return "Access denied to options file. Please run as administrator or check file permissions ❗";
            }
            catch (IOException ex)
            {
                return $"File is in use or inaccessible: {ex.Message}";
            }

            // Remove read-only attribute
            try
            {
                var fileInfo = new FileInfo(optionsFilePath);
                if (fileInfo.IsReadOnly)
                {
                    fileInfo.IsReadOnly = false;
                }
            }
            catch (Exception ex)
            {
                return $"Failed to remove readonly attribute from options.txt file: {ex.Message} ❗";
            }

            // Update graphics mode
            try
            {
                var lines = await File.ReadAllLinesAsync(optionsFilePath);
                var statusMessages = new List<string>();
                bool graphicsModeFound = false;

                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("graphics_mode:", StringComparison.OrdinalIgnoreCase))
                    {
                        var oldValue = lines[i];
                        lines[i] = "graphics_mode:3";
                        graphicsModeFound = true;
                        statusMessages.Add($"{oldValue} -> {lines[i]}");
                        break;
                    }
                }

                if (!graphicsModeFound)
                {
                    statusMessages.Add("Graphics mode setting not found in options file. Adding it...");
                    var linesList = lines.ToList();
                    linesList.Add("graphics_mode:3");
                    lines = linesList.ToArray();
                }

                // Disable VSync too while we're at it
                bool vsyncFound = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("gfx_vsync:", StringComparison.OrdinalIgnoreCase))
                    {
                        var oldVSyncValue = lines[i];
                        lines[i] = "gfx_vsync:0";
                        vsyncFound = true;
                        statusMessages.Add($"VSync: {oldVSyncValue} -> {lines[i]}");
                        break;
                    }
                }
                if (!vsyncFound)
                {
                    statusMessages.Add("VSync setting not found. Adding it...");
                    var linesList = lines.ToList();
                    linesList.Add("gfx_vsync:0");
                    lines = linesList.ToArray();
                }

                // Create backup before writing
                var backupPath = optionsFilePath + ".backup";
                File.Copy(optionsFilePath, backupPath, true);
                statusMessages.Add("Created backup of options file.");

                await File.WriteAllLinesAsync(optionsFilePath, lines);
                statusMessages.Add("Options file updated successfully.");

                // Delay just in case (miliseconds)
                await Task.Delay(333);

                // Launch Minecraft depending on protocol
                try
                {
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = protocol,
                        UseShellExecute = true,
                        ErrorDialog = false
                    };

                    Process.Start(processInfo);
                    statusMessages.Add($"Ray tracing enabled and launched {versionName} successfully.");
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    statusMessages.Add($"Failed to launch {versionName}: {ex.Message}");
                    statusMessages.Add("Make sure the game is installed and the minecraft:// protocols are allowed to work.");
                }
                catch (Exception ex)
                {
                    statusMessages.Add($"Unexpected error launching {versionName}: {ex.Message}");
                }

                return string.Join("\n", statusMessages);
            }
            catch (Exception ex)
            {
                return $"Failed to update options file: {ex.Message}";
            }
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}