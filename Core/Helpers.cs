using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using ImageMagick;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Windows.Storage.Pickers;
using static Vanilla_RTX_Tuner_WinUI.MainWindow;

namespace Vanilla_RTX_Tuner_WinUI.Core;

public static class Helpers
{
    // TODO: These are copied over from the Toolkit, we only deal with Vanilla RTX files here, simplify
    public static Bitmap ReadImage(string imagePath, bool maxOpacity = false)
    {
        try
        {
            using (var sourceImage = new MagickImage(imagePath))
            {
                var width = (int)sourceImage.Width;
                var height = (int)sourceImage.Height;


                var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                using (var sourcePixels = sourceImage.GetPixels())
                {

                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            var pixelData = sourcePixels.GetPixel(x, y);

                            byte r, g, b, a;

                            var hasAlpha = sourceImage.HasAlpha || sourceImage.ColorType == ColorType.GrayscaleAlpha || sourceImage.ColorType == ColorType.TrueColorAlpha;

                            if (sourceImage.ColorType == ColorType.Grayscale)
                            {
                                var gray = (byte)(pixelData[0] >> 8);
                                r = g = b = gray;
                                a = 255;
                            }
                            else if (sourceImage.ColorType == ColorType.GrayscaleAlpha)
                            {
                                var gray = (byte)(pixelData[0] >> 8);
                                r = g = b = gray;
                                var originalAlpha = (byte)(pixelData[1] >> 8);
                                a = maxOpacity ? (byte)255 : originalAlpha;
                            }
                            else if (sourceImage.ColorType == ColorType.TrueColor)
                            {
                                r = (byte)(pixelData[0] >> 8);
                                g = (byte)(pixelData[1] >> 8);
                                b = (byte)(pixelData[2] >> 8);
                                a = 255;
                            }
                            else if (sourceImage.ColorType == ColorType.TrueColorAlpha)
                            {
                                r = (byte)(pixelData[0] >> 8);
                                g = (byte)(pixelData[1] >> 8);
                                b = (byte)(pixelData[2] >> 8);
                                var originalAlpha = (byte)(pixelData[3] >> 8);
                                a = maxOpacity ? (byte)255 : originalAlpha;
                            }
                            else if (sourceImage.ColorType == ColorType.Palette)
                            {
                                r = (byte)(pixelData[0] >> 8);
                                g = (byte)(pixelData[1] >> 8);
                                b = (byte)(pixelData[2] >> 8);

                                if (hasAlpha && sourceImage.ChannelCount > 3)
                                {
                                    var originalAlpha = (byte)(pixelData[3] >> 8);
                                    a = maxOpacity ? (byte)255 : originalAlpha;
                                }
                                else
                                {
                                    a = 255;
                                }
                            }
                            else
                            {
                                var channels = (int)sourceImage.ChannelCount;

                                r = channels > 0 ? (byte)(pixelData[0] >> 8) : (byte)0;
                                g = channels > 1 ? (byte)(pixelData[1] >> 8) : r;
                                b = channels > 2 ? (byte)(pixelData[2] >> 8) : r;

                                if (hasAlpha && channels > 3)
                                {
                                    var originalAlpha = (byte)(pixelData[3] >> 8);
                                    a = maxOpacity ? (byte)255 : originalAlpha;
                                }
                                else
                                {
                                    a = 255;
                                }
                            }
                            var pixelColor = Color.FromArgb(a, r, g, b);
                            bitmap.SetPixel(x, y, pixelColor);
                        }
                    }
                }

                return bitmap;
            }
        }
        catch (Exception ex)
        {
            // PushLog($"Error reading image {imagePath}: {ex.Message}");
            var errorBitmap = new Bitmap(512, 512, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(errorBitmap))
            {
                g.Clear(Color.Transparent);
                var squareSize = 256;
                g.FillRectangle(new SolidBrush(Color.FromArgb(255, 77, 172, 255)), 0, 0, squareSize, squareSize);
                g.FillRectangle(new SolidBrush(Color.FromArgb(255, 0, 35, 66)), squareSize, 0, squareSize, squareSize);
                g.FillRectangle(new SolidBrush(Color.FromArgb(255, 0, 35, 66)), 0, squareSize, squareSize, squareSize);
                g.FillRectangle(new SolidBrush(Color.FromArgb(255, 77, 172, 255)), squareSize, squareSize, squareSize, squareSize);
            }
            return errorBitmap;
        }
    }
    public static void WriteImageAsTGA(Bitmap bitmap, string outputPath)
    {
        try
        {
            var width = bitmap.Width;
            var height = bitmap.Height;

            // Write TGA file format manually for absolute control
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(fs))
            {
                // TGA
                writer.Write((byte)0);    // ID Length
                writer.Write((byte)0);    // Color Map Type (0 = no color map)
                writer.Write((byte)2);    // Image Type (2 = uncompressed RGB)
                writer.Write((ushort)0);  // Color Map First Entry Index
                writer.Write((ushort)0);  // Color Map Length
                writer.Write((byte)0);    // Color Map Entry Size
                writer.Write((ushort)0);  // X-origin
                writer.Write((ushort)0);  // Y-origin
                writer.Write((ushort)width);  // Width
                writer.Write((ushort)height); // Height
                writer.Write((byte)32);       // Pixel Depth (32-bit RGBA)
                writer.Write((byte)8);        // Image Descriptor (default origin, 8-bit alpha)

                for (var y = height - 1; y >= 0; y--) // TGA is bottom-up by default
                {
                    for (var x = 0; x < width; x++)
                    {
                        var pixel = bitmap.GetPixel(x, y);

                        writer.Write(pixel.B);
                        writer.Write(pixel.G);
                        writer.Write(pixel.R);
                        writer.Write(pixel.A);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // PushLog($"Error writing direct TGA to {outputPath}: {ex.Message}");
            throw;
        }
    }


    // TODO: Get rid of this, use the internal thing, a new method for it.
    public static T GetConfig<T>(string paramName)
    {
        var jsonFilePath = Path.Combine(AppContext.BaseDirectory, "Assets", "config.json");
        var jsonContent = File.ReadAllText(jsonFilePath);

        var jsonObject = JsonConvert.DeserializeObject<JObject>(jsonContent);

        foreach (var group in jsonObject)
        {
            if (group.Value is JObject settingsGroup)
            {
                JToken parameterValue;
                if (settingsGroup.TryGetValue(paramName, out parameterValue))
                {
                    try
                    {
                        return parameterValue.ToObject<T>();
                    }
                    catch (Exception ex)
                    {
                        PushLog($"Error returning parameter value for '{paramName}' to type {typeof(T)}: {ex.Message}");
                    }
                }
            }
        }
        return default;
    }



    public static async Task<(bool, string?)> Download(string url)
    {
        var retries = 3;
        while (retries-- > 0)
        {
            try
            {
                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(15)
                };
                var userAgent = $"vanilla_rtx_tuner/{TunerVariables.appVersion}";
                client.DefaultRequestHeaders.Add("User-Agent", userAgent);

                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                PushLog("Starting Download.");

                var totalBytes = response.Content.Headers.ContentLength;
                if (!totalBytes.HasValue)
                    PushLog("Total file size unknown. Progress will be logged as total downloaded (in MegaBytes).");

                // Get filename
                string fileName;
                if (response.Content.Headers.ContentDisposition?.FileName != null)
                {
                    fileName = response.Content.Headers.ContentDisposition.FileName.Trim('"');
                }
                else
                {
                    fileName = Path.GetFileName(new Uri(url).AbsolutePath);
                    if (string.IsNullOrEmpty(fileName))
                        fileName = "downloaded_file";
                    PushLog("File name: " + fileName);
                }

                // Sanitize filename
                fileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));

                // fallback locations in order: temp dir, user dl folder, app's data dir, app's dir, desktop as last resort
                string savingLocation = null;
                var fallbackLocations = new Func<string>[]
                {
                () => Path.Combine(Path.GetTempPath(), "vanilla_rtx_tuner_cache", fileName),
                
                () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads", "vanilla_rtx_tuner_cache", fileName),
                
                () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "vanilla_rtx_tuner_cache", fileName),

                () => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vanilla_rtx_tuner_cache", fileName),
                
                () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "vanilla_rtx_tuner_cache", fileName)
                };

                foreach (var getPath in fallbackLocations)
                {
                    try
                    {
                        var testPath = getPath();
                        var testDir = Path.GetDirectoryName(testPath);

                        Directory.CreateDirectory(testDir);

                        // Test write access with a temp file
                        var testFile = Path.Combine(testDir, $"tuner_write_test_{Guid.NewGuid()}.tmp");
                        File.WriteAllText(testFile, "tuner_write_test");
                        File.Delete(testFile);

                        savingLocation = testPath;
                        if (testPath != fallbackLocations[2]())
                        {
                            PushLog($"Using save location: {savingLocation}");
                        }
                        break;
                    }
                    catch
                    {
                        continue;
                    }
                }

                if (savingLocation == null)
                {
                    PushLog("No writable location found for download.");
                    return (false, null);
                }

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(savingLocation, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int read;
                double lastLoggedProgress = 0;
                var lastLoggedMB = 0;

                while ((read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length))) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read));
                    totalRead += read;

                    if (totalBytes.HasValue)
                    {
                        var progress = (double)totalRead / totalBytes.Value * 100;
                        if (progress - lastLoggedProgress >= 10 || progress >= 100)
                        {
                            lastLoggedProgress = progress;
                            PushLog($"Download Progress: {progress:0}%");
                        }
                    }
                    else
                    {
                        var currentMB = (int)(totalRead / (1024 * 1024));
                        if (currentMB > lastLoggedMB)
                        {
                            lastLoggedMB = currentMB;
                            PushLog($"Download Progress: {currentMB} MB");
                        }
                    }
                }

                PushLog("Download finished successfully.");
                return (true, savingLocation);
            }
            catch (HttpRequestException ex) when (retries > 0)
            {
                PushLog($"Transient error: {ex.Message}. Retrying...");
                await Task.Delay(1000);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException && retries > 0)
            {
                PushLog("Request timed out. Retrying...");
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                PushLog($"Error during download: {ex.Message}");
                return (false, null);
            }
        }

        PushLog("Download failed after multiple attempts.");
        return (false, null);
    }


    // TODO: Make it smarter, don't clean up and redownload every time, cache the zip, compare versions, and download a new version only if the one on github is higher OR with a 30 minute delay
    // The logic for Vanilla RTX reinstallation (0-100, dl to deploy) could be handled in a cleaner way, move it to a separate file -- for now this works.
    // Use this API call for downloading the repo: https://api.github.com/repos/Cubeir/Vanilla-RTX/zipball/master
    public static async Task ExtractAndDeployPacks(string saveLocation)
    {
        await Task.Run(() =>
        {
            try
            {
                var saveDir = Path.GetDirectoryName(saveLocation)!;
                var stagingDir = Path.Combine(saveDir, "_staging");
                var extractDir = Path.Combine(stagingDir, "extracted");

                // Fallback logic is already applied during download, and the final save location is passed to this method
                // This just guards in case its dir somehow vanishes before we get to deploying
                // It likely won't be used, but if it is desktop is good -- makes it easy to notice.
                if (!Directory.Exists(saveDir))
                {
                    saveDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "vanilla_rtx_tuner_fallback");
                    stagingDir = Path.Combine(saveDir, "_staging");
                    extractDir = Path.Combine(stagingDir, "extracted");

                    Directory.CreateDirectory(saveDir);
                    PushLog($"Fallback: Using desktop for staging: {saveDir}");
                }

                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, true);

                if (File.Exists(saveLocation))
                {
                    ZipFile.ExtractToDirectory(saveLocation, extractDir, overwriteFiles: true);
                }
                
                var resourcePackPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Packages",
                    TunerVariables.IsTargetingPreview ? "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe" : "Microsoft.MinecraftUWP_8wekyb3d8bbwe",
                    "LocalState", "games", "com.mojang", "resource_packs");

                if (!Directory.Exists(resourcePackPath))
                {
                    PushLog("Resource pack directory not found. Is the correct Minecraft version installed?");
                    return;
                }

                void ForceWritable(string path)
                {
                    var di = new DirectoryInfo(path);
                    if (di.Exists)
                    {
                        di.Attributes &= ~FileAttributes.ReadOnly;
                        foreach (var file in di.GetFiles("*", SearchOption.AllDirectories))
                            file.Attributes &= ~FileAttributes.ReadOnly;
                        foreach (var dir in di.GetDirectories("*", SearchOption.AllDirectories))
                            dir.Attributes &= ~FileAttributes.ReadOnly;
                    }
                }

                ForceWritable(resourcePackPath);

                var manifestFiles = Directory.GetFiles(resourcePackPath, "manifest.json", SearchOption.AllDirectories);
                foreach (var file in manifestFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        dynamic data = JsonConvert.DeserializeObject(json);

                        string headerUUID = data?.header?.uuid;
                        string moduleUUID = data?.modules?[0]?.uuid;
                        var folder = Path.GetDirectoryName(file)!;

                        if (headerUUID == "a5c3cc7d-1740-4b5e-ae2c-71bc14b3f63b" &&
                             moduleUUID == "af805084-fafa-4124-9ae2-00be4bc202dc" ||
                            headerUUID == "bbe2b225-b45b-41c2-bd3b-465cd83e6071" &&
                             moduleUUID == "b2eef2c6-d893-467e-b31d-cda7bf643eaa")
                        {
                            ForceWritable(folder);
                            Directory.Delete(folder, true);
                            PushLog($"Removed existing pack: {Path.GetFileName(folder)}");
                        }
                    }
                    catch { continue; }
                }

                var vanillaRoot = Path.Combine(extractDir, "Vanilla-RTX-master");

                void CopyAndRename(string folderName)
                {
                    var src = Path.Combine(vanillaRoot, folderName);
                    if (!Directory.Exists(src))
                    {
                        PushLog($"Missing folder: {folderName}");
                        return;
                    }

                    var baseName = folderName.Replace("-", "");
                    var dst = Path.Combine(resourcePackPath, baseName);
                    var suffix = 1;

                    while (Directory.Exists(dst))
                    {
                        var manifest = Path.Combine(dst, "manifest.json");
                        if (File.Exists(manifest))
                        {
                            try
                            {
                                var json = File.ReadAllText(manifest);
                                dynamic data = JsonConvert.DeserializeObject(json);

                                string headerUUID = data?.header?.uuid;
                                string moduleUUID = data?.modules?[0]?.uuid;

                                var isOurPack =
                                    headerUUID == "a5c3cc7d-1740-4b5e-ae2c-71bc14b3f63b" &&
                                     moduleUUID == "af805084-fafa-4124-9ae2-00be4bc202dc" ||
                                    headerUUID == "bbe2b225-b45b-41c2-bd3b-465cd83e6071" &&
                                     moduleUUID == "b2eef2c6-d893-467e-b31d-cda7bf643eaa";

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
                    PushLog($"Deployed: {Path.GetFileName(dst)}");
                }

                CopyAndRename("Vanilla-RTX");
                CopyAndRename("Vanilla-RTX-Normals");

                // Cleanup
                try
                {
                    if (Directory.Exists(stagingDir))
                    {
                        ForceWritable(stagingDir);
                        Directory.Delete(stagingDir, true);
                        PushLog($"Deleted staging directory: {stagingDir}");
                    }

                    if (File.Exists(saveLocation))
                    {
                        File.Delete(saveLocation);
                        PushLog($"Deleted downloaded file: {saveLocation}");
                    }

                    var parent = Path.GetDirectoryName(saveLocation)!;
                    if (parent.EndsWith("vanilla_rtx_tuner", StringComparison.OrdinalIgnoreCase) ||
                        parent.EndsWith("vanilla_rtx_tuner_fallback", StringComparison.OrdinalIgnoreCase))
                    {
                        ForceWritable(parent);
                        Directory.Delete(parent, true);
                        PushLog($"Deleted download folder: {parent}");
                    }
                }
                catch (Exception ex)
                {
                    PushLog($"Warning: Cleanup failed – {ex.Message}");
                }

                PushLog("Deployment complete. You can now relocate & tune the fresh installations");
            }
            catch (Exception ex)
            {
                PushLog($"Deployment failed: {ex.Message}");
            }
        });
    }
    private static void DirectoryCopy(string sourceDir, string destDir, bool copySubDirs)
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



    public static async Task MCPackExporter(string packFolderPath, string suggestedName)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(Instance);
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.FileTypeChoices.Add("Minecraft Pack", new List<string>() { ".mcpack" });
        picker.SuggestedFileName = suggestedName;
        picker.SuggestedStartLocation = PickerLocationId.Desktop;

        var unneededFiles = new[] { "contents.json", "textures_list.json" };
        foreach (var unneededFile in Directory.GetFiles(packFolderPath, "*", SearchOption.TopDirectoryOnly))
        {
            if (unneededFiles.Contains(Path.GetFileName(unneededFile), StringComparer.OrdinalIgnoreCase))
                File.Delete(unneededFile);
        }

        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        var tempZipPath = Path.Combine(Path.GetTempPath(), $"temp_{Guid.NewGuid()}.mcpack");

        try
        {
            using (var zip = ZipFile.Open(tempZipPath, ZipArchiveMode.Create))
            {
                var baseFolder = Path.GetFileName(packFolderPath.TrimEnd(Path.DirectorySeparatorChar));
                foreach (var filePath in Directory.GetFiles(packFolderPath, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.Combine(baseFolder, Path.GetRelativePath(packFolderPath, filePath));
                    zip.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Optimal);
                }
            }

            if (!File.Exists(tempZipPath))
            {
                // MainWindow.PushLog($"Temporary .mcpack archive was deleted before writing to output.");
                return;
            }

            using var destStream = await file.OpenStreamForWriteAsync();
            using var srcStream = File.OpenRead(tempZipPath);
            await srcStream.CopyToAsync(destStream);

            // MainWindow.PushLog($"{suggestedName}.mcpack exported successfully.");
        }
        catch (Exception ex)
        {
            // MainWindow.PushLog($"Failed to export {suggestedName}: {ex.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(tempZipPath))
                    File.Delete(tempZipPath);
            }
            catch (Exception ex)
            {
                // MainWindow.PushLog($"Warning: Couldn't delete temp file: {ex.Message}");
            }
        }
    }
}