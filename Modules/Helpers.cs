using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;
using Windows.ApplicationModel;
using static Vanilla_RTX_Tuner_WinUI.MainWindow;

namespace Vanilla_RTX_Tuner_WinUI.Modules;

public static class Helpers
{
    public static Bitmap ReadImage(string imagePath, bool maxOpacity = false)
    {
        try
        {
            using var sourceImage = new MagickImage(imagePath);
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
        catch (Exception)
        {
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
            using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using var writer = new BinaryWriter(fs);
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
        catch (Exception ex)
        {
            // Log($"Error writing direct TGA to {outputPath}: {ex.Message}");
            throw;
        }
    }


    private static readonly HttpClient SharedHttpClient = new();
    public static async Task<(bool, string?)> Download(string url, CancellationToken cancellationToken = default)
    {
        var retries = 3;
        while (retries-- > 0)
        {
            try
            {
                // === HTTP CLIENT CONFIGURATION ===
                SharedHttpClient.Timeout = TimeSpan.FromSeconds(15);
                var userAgent = $"vanilla_rtx_tuner/{TunerVariables.appVersion}";
                if (!SharedHttpClient.DefaultRequestHeaders.Contains("User-Agent"))
                {
                    SharedHttpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
                }

                // === DOWNLOAD ===
                using var response = await SharedHttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                Log("Starting Download.", LogLevel.Lengthy);

                var totalBytes = response.Content.Headers.ContentLength;
                if (!totalBytes.HasValue)
                    Log("Total file size unknown. Progress will be logged as total downloaded (in MegaBytes).", LogLevel.Informational);

                // === FILENAME EXTRACTION AND SANITIZATION ===
                string fileName;
                if (response.Content.Headers.ContentDisposition?.FileName != null)
                {
                    fileName = response.Content.Headers.ContentDisposition.FileName.Trim('"');
                }
                else
                {
                    fileName = Path.GetFileName(new Uri(url).AbsolutePath);
                    if (string.IsNullOrEmpty(fileName))
                    {
                        // Fallback to random UUID filename if no valid name found
                        fileName = $"download_{Guid.NewGuid():N}";
                        Log($"No valid filename found, using random name: {fileName}", LogLevel.Informational);
                    }
                    else
                    {
                        Log("File name: " + fileName, LogLevel.Informational);
                    }
                }

                // sanitize filename
                fileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
                if (fileName.Length > 128) fileName = fileName.Substring(0, 128);

                // === LOCATION RESOLUTION WITH FALLBACK === Temp dir, user dl folder, app's data dir, app's dir, desktop as last resort
                string? savingLocation = null;
                var fallbackLocations = new Func<string>[]
                {
                () => Path.Combine(Path.GetTempPath(), TunerVariables.CacheFolderName, fileName),
                () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), TunerVariables.CacheFolderName, fileName),
                () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), TunerVariables.CacheFolderName, fileName),
                () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), TunerVariables.CacheFolderName, fileName),
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

                        // Check if file exists and generate unique name if needed
                        var finalPath = testPath;
                        var counter = 1;
                        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(testPath);
                        var extension = Path.GetExtension(testPath);
                        var directory = Path.GetDirectoryName(testPath);

                        while (File.Exists(finalPath))
                        {
                            var newFileName = $"{fileNameWithoutExt}-{counter}{extension}";
                            finalPath = Path.Combine(directory, newFileName);
                            counter++;
                        }

                        savingLocation = finalPath;
                        if (testPath != fallbackLocations[2]())
                        {
                            Log($"Save location: {savingLocation}", LogLevel.Informational);
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
                    Log("No writable location found for download.", LogLevel.Error);
                    return (false, null);
                }

                // === DOWNLOAD WITH PROGRESS TRACKING ===
                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(savingLocation, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int read;
                double lastLoggedProgress = 0;
                var lastLoggedMB = 0;

                while ((read = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    totalRead += read;

                    if (totalBytes.HasValue)
                    {
                        var progress = (double)totalRead / totalBytes.Value * 100;
                        if (progress - lastLoggedProgress >= 10 || progress >= 100)
                        {
                            lastLoggedProgress = progress;
                            Log($"Download Progress: {progress:0}%", LogLevel.Informational);
                        }
                    }
                    else
                    {
                        var currentMB = (int)(totalRead / (1024 * 1024));
                        if (currentMB > lastLoggedMB)
                        {
                            lastLoggedMB = currentMB;
                            Log($"Download Progress: {currentMB} MB", LogLevel.Informational);
                        }
                    }
                }

                Log("Download finished successfully.", LogLevel.Success);
                return (true, savingLocation);
            }
            catch (OperationCanceledException ex) when (!(ex is TaskCanceledException) || ex.InnerException is not TimeoutException)
            {
                Log("Download cancelled by user.", LogLevel.Informational);
                return (false, null);
            }
            catch (HttpRequestException ex) when (retries > 0)
            {
                Log($"Transient error: {ex.Message}. Retrying...", LogLevel.Warning);
                await Task.Delay(1000, cancellationToken);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException && retries > 0)
            {
                Log("Request timed out. Retrying...", LogLevel.Warning);
                await Task.Delay(1000, cancellationToken);
            }
            catch (Exception ex)
            {
                Log($"Error during download: {ex.Message}", LogLevel.Error);
                return (false, null);
            }
        }

        Log("Download failed after multiple attempts.", LogLevel.Error);
        return (false, null);
    }
    public static string GetCacheFolderName()
    {
        try
        {
            var family = Windows.ApplicationModel.Package.Current.Id.FamilyName;
            var idx = family.LastIndexOf('_');
            var suffix = (idx >= 0 && idx < family.Length - 1)
                ? family[(idx + 1)..]
                : family;
            return $"tuner_{suffix}";
        }
        catch
        {
            return "vanilla_rtx_tuner_cache";
        }
    }

}



/// <summary>
/// Additional helper to do a thing only once per runtime, use RanOnceFlag.Set("key") to set a flag with a unique key.
/// </summary>
public static class RuntimeFlags
{
    private static readonly HashSet<string> _flags = new();

    public static bool Has(string key) => _flags.Contains(key); // Below does the same as this one

    public static bool Set(string key)
    {
        if (_flags.Contains(key))
            return false;

        _flags.Add(key);
        return true;
    }

    public static bool Unset(string key) => _flags.Remove(key);
}

