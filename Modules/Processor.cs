using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using static Vanilla_RTX_Tuner_WinUI.TunerVariables;
using static Vanilla_RTX_Tuner_WinUI.TunerVariables.Persistent;
using static Vanilla_RTX_Tuner_WinUI.Modules.Helpers;
using static Vanilla_RTX_Tuner_WinUI.Modules.ProcessorVariables;
using System.Diagnostics;

namespace Vanilla_RTX_Tuner_WinUI.Modules;

public static class ProcessorVariables
{
    public const bool FOG_UNIFORM_HEIGHT = false;
    public const double FOG_EXCESS_DENSITY_DAMPEN = 0.001;
    public const double FOG_EXCESS_DENSITY_TO_SCATTER_DAMPEN = 0.005;
    public const double FOG_EXCESS_SCATTERING_DAMPEN = 0.005;
    public const double EMISSIVE_EXCESS_INTENSITY_DAMPEN = 0.1;
}


// TODO: Idea was to refactor the processor so it loads all files first, then processes them in multiple passes in memory instead of
// constantly loading and saving, but the tuning already happens quite fast (with the files being raw tgas) so it may not be worth
// the added complexity of defining which textures will be needed to be retrieved and all that
// Still, if a kind soul out there wants to take a stab at it, be my guest.

public class Processor
{
    private struct PackInfo
    {
        public string Name;
        public string Path;
        public bool Enabled;

        public PackInfo(string name, string path, bool enabled)
        {
            Name = name;
            Path = path;
            Enabled = enabled;
        }
    }

    public static void TuneSelectedPacks()
    {
        MainWindow.Log("Options left at default will be skipped.", MainWindow.LogLevel.Informational);
        MainWindow.Log("Tuning selected packages...", MainWindow.LogLevel.Lengthy);

        var packs = new[]
        {
        new PackInfo("Vanilla RTX", VanillaRTXLocation, IsVanillaRTXEnabled),
        new PackInfo("Vanilla RTX Normals", VanillaRTXNormalsLocation, IsNormalsEnabled),
        new PackInfo("Vanilla RTX Opus", VanillaRTXOpusLocation, IsOpusEnabled),
        new PackInfo(CustomPackDisplayName, CustomPackLocation, true)
    };



        // Removes custom pack from the array if its path duplicates an already selected pack
        string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        var customPackNormalizedPath = NormalizePath(CustomPackLocation);

        bool isDuplicate = packs
            .Take(packs.Length - 1)
            .Where(p => p.Enabled)
            .Any(p => NormalizePath(p.Path).Equals(customPackNormalizedPath, StringComparison.OrdinalIgnoreCase));

        // Remove custom pack path if it's a duplicate of an already selected pack
        if (isDuplicate)
        {
            MainWindow.Log($"{CustomPackDisplayName} was selected twice, but will only be processed once!", MainWindow.LogLevel.Warning);
            packs = packs.Take(packs.Length - 1).ToArray();
        }




        if (FogMultiplier != Defaults.FogMultiplier)
        {
            foreach (var p in packs)
                ProcessFog(p);
        }

        if (EmissivityMultiplier != Defaults.EmissivityMultiplier || AddEmissivityAmbientLight != Defaults.AddEmissivityAmbientLight)
        {
            foreach (var p in packs)
                ProcessEmissivity(p);
        }

        if (NormalIntensity != Defaults.NormalIntensity)
        {
            foreach (var p in packs)
                ProcessNormalIntensity(p);
        }

        if (MaterialNoiseOffset != Defaults.MaterialNoiseOffset)
        {
            foreach (var p in packs)
                ProcessMaterialNoise(p);
        }

        if (RoughenUpIntensity != Defaults.RoughenUpIntensity)
        {
            foreach (var p in packs)
                ProcessRoughingUp(p);
        }

        if (ButcheredHeightmapAlpha != Defaults.ButcheredHeightmapAlpha)
        {
            foreach (var p in packs)
                ProcessHeightmaps(p);
        }
    }


    // TODO: make processors return reasons of their failure for easier debugging at the end without touching UI thread directly.
    // this is got to become a part of the larger logging overhaul down the line (gradual logger thing from public string)
    // Also make them log any unexpected oddities in Vanilla RTX (whether it be size, opacity, etc...) as warnings
    #region ------------------- Processors
    private static void ProcessFog(PackInfo pack, bool processWaterOnly = false)
    {
        if (string.IsNullOrEmpty(pack.Path) || !Directory.Exists(pack.Path))
            return;

        // Find all directories named "fogs" within the pack path
        var fogDirectories = Directory
            .GetDirectories(pack.Path, "*", SearchOption.AllDirectories)
            .Where(d => string.Equals(Path.GetFileName(d), "fogs", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (!fogDirectories.Any())
        {
            MainWindow.Log($"{pack.Name}: does not contain fog files.");
            return;
        }

        var files = new List<string>();

        // Collect all JSON files from all "fogs" directories
        foreach (var fogDir in fogDirectories)
        {
            try
            {
                var jsonFiles = Directory.GetFiles(fogDir, "*.json", SearchOption.TopDirectoryOnly);
                files.AddRange(jsonFiles);
            }
            catch
            {
                // Skip directories that can't be accessed
            }
        }

        if (!files.Any())
        {
            return;
        }

        foreach (var file in files)
        {
            try
            {
                var text = File.ReadAllText(file);
                var root = JObject.Parse(text);

                var fogSettings = root.SelectToken("minecraft:fog_settings") as JObject;
                var volumetric = fogSettings?.SelectToken("volumetric") as JObject;

                if (volumetric == null)
                {
                    continue;
                }

                var modified = false;

                if (processWaterOnly)
                {
                    // Only process water coefficients
                    modified = ProcessWaterCoefficients(volumetric);
                }
                else
                {
                    // Process air densities and scattering (existing logic)
                    var density = volumetric.SelectToken("density") as JObject;

                    if (density != null)
                    {
                        // Extract density sections
                        var airSection = density.SelectToken("air") as JObject;
                        var weatherSection = density.SelectToken("weather") as JObject;

                        var densityValues = new List<(string name, JObject section, double originalValue, double multipliedValue)>();
                        var anyDensityWasMaxed = false;

                        // Process air density
                        if (airSection != null)
                        {
                            var airMaxDensityToken = airSection.SelectToken("max_density");
                            if (airMaxDensityToken != null && TryGetDensityValue(airMaxDensityToken, out var airDensity))
                            {
                                // Check if it was already maxed before we touch it
                                if (Math.Abs(airDensity - 1.0) < 0.0000001)
                                {
                                    anyDensityWasMaxed = true;
                                }

                                // Apply special logic for near-zero densities
                                if (Math.Abs(airDensity) < 0.0001)
                                {
                                    var newDensity = CalculateNewDensity(airDensity, FogMultiplier);
                                    if (Math.Abs(newDensity - airDensity) >= 0.0000001)
                                    {
                                        airSection["max_density"] = Math.Round(newDensity, 7);
                                        modified = true;
                                    }
                                }
                                else
                                {
                                    // Normal multiplication - let it run free
                                    var multipliedValue = airDensity * FogMultiplier;
                                    densityValues.Add(("air", airSection, airDensity, multipliedValue));
                                }
                            }
                        }

                        // Process weather density
                        if (weatherSection != null)
                        {
                            var weatherMaxDensityToken = weatherSection.SelectToken("max_density");
                            if (weatherMaxDensityToken != null && TryGetDensityValue(weatherMaxDensityToken, out var weatherDensity))
                            {
                                // Check if it was already maxed before we touch it
                                if (Math.Abs(weatherDensity - 1.0) < 0.0000001)
                                {
                                    anyDensityWasMaxed = true;
                                }

                                // Apply special logic for near-zero densities
                                if (Math.Abs(weatherDensity) < 0.0001)
                                {
                                    var newDensity = CalculateNewDensity(weatherDensity, FogMultiplier);
                                    if (Math.Abs(newDensity - weatherDensity) >= 0.0000001)
                                    {
                                        weatherSection["max_density"] = Math.Round(newDensity, 7);
                                        modified = true;
                                    }
                                }
                                else
                                {
                                    // Normal multiplication - let it run free
                                    var multipliedValue = weatherDensity * FogMultiplier;
                                    densityValues.Add(("weather", weatherSection, weatherDensity, multipliedValue));
                                }
                            }
                        }

                        // Apply proportional scaling to densities if needed
                        if (densityValues.Any())
                        {
                            var maxMultipliedValue = densityValues.Max(x => x.multipliedValue);

                            // Scale down if any value exceeds 1.0
                            if (maxMultipliedValue > 1.0)
                            {
                                var scaleFactor = 1.0 / maxMultipliedValue;
                                foreach (var (name, section, originalValue, multipliedValue) in densityValues)
                                {
                                    var scaledValue = multipliedValue * scaleFactor;
                                    section["max_density"] = Math.Round(scaledValue, 7);
                                    modified = true;
                                }
                            }
                            else
                            {
                                // No scaling needed, just apply the multiplied values
                                foreach (var (name, section, originalValue, multipliedValue) in densityValues)
                                {
                                    section["max_density"] = Math.Round(multipliedValue, 7);
                                    modified = true;
                                }
                            }
                        }

                        // Handle overflow to scattering if any density was already maxed
                        if (anyDensityWasMaxed)
                        {
                            // Calculate dampened multiplier (75% dampening towards 1.0)
                            var overage = FogMultiplier - 1.0;
                            var dampenedOverage = overage * 0.25; // 75% dampening = keep only 25% of overage
                            var scatteringMultiplier = 1.0 + dampenedOverage;

                            var mediaCoefficients = volumetric.SelectToken("media_coefficients") as JObject;
                            var airCoefficients = mediaCoefficients?.SelectToken("air") as JObject;

                            if (airCoefficients != null)
                            {
                                var scatteringToken = airCoefficients.SelectToken("scattering") as JArray;
                                if (scatteringToken != null && scatteringToken.Count >= 3)
                                {
                                    var rgbValues = new double[3];
                                    var allValid = true;

                                    // Get current RGB values and multiply freely
                                    for (var i = 0; i < 3; i++)
                                    {
                                        if (TryGetDensityValue(scatteringToken[i], out var value))
                                        {
                                            rgbValues[i] = value * scatteringMultiplier;
                                        }
                                        else
                                        {
                                            allValid = false;
                                            break;
                                        }
                                    }

                                    if (allValid)
                                    {
                                        // Find max value
                                        var maxRgb = rgbValues.Max();

                                        // Scale down proportionally if any exceeds 1.0
                                        if (maxRgb > 1.0)
                                        {
                                            var scaleFactor = 1.0 / maxRgb;
                                            for (var i = 0; i < 3; i++)
                                            {
                                                rgbValues[i] *= scaleFactor;
                                            }
                                        }

                                        // Apply the values
                                        for (var i = 0; i < 3; i++)
                                        {
                                            scatteringToken[i] = Math.Round(rgbValues[i], 7);
                                        }
                                        modified = true;
                                    }
                                }
                            }
                        }

                        // Process uniform density settings
                        modified |= ProcessDensitySection(density, "air", FOG_UNIFORM_HEIGHT);
                        modified |= ProcessDensitySection(density, "weather", FOG_UNIFORM_HEIGHT);
                    }
                }

                if (modified)
                {
                    File.WriteAllText(file, root.ToString(Newtonsoft.Json.Formatting.Indented));
                }
            }
            catch (Exception ex)
            {
                // MainWindow.Log($"{pack.Name}: error processing {Path.GetFileName(file)} — {ex.Message}");
            }
        }

        bool ProcessWaterCoefficients(JObject volumetric)
        {
            var modified = false;

            // Calculate 90% dampened multiplier towards 1.0
            var overage = FogMultiplier - 1.0;
            var dampenedOverage = overage * 0.1; // 90% dampening = keep only 10% of overage
            var waterMultiplier = 1.0 + dampenedOverage;

            var mediaCoefficients = volumetric.SelectToken("media_coefficients") as JObject;
            var waterCoefficients = mediaCoefficients?.SelectToken("water") as JObject;

            if (waterCoefficients == null)
                return false;

            // Process water scattering
            var scatteringToken = waterCoefficients.SelectToken("scattering") as JArray;
            if (scatteringToken != null && scatteringToken.Count >= 3)
            {
                modified |= ProcessRgbArray(scatteringToken, waterMultiplier);
            }

            // Process water absorption
            var absorptionToken = waterCoefficients.SelectToken("absorption") as JArray;
            if (absorptionToken != null && absorptionToken.Count >= 3)
            {
                modified |= ProcessRgbArray(absorptionToken, waterMultiplier);
            }

            return modified;
        }

        bool ProcessRgbArray(JArray rgbArray, double multiplier)
        {
            var rgbValues = new double[3];
            var allValid = true;

            // Get current RGB values and multiply freely
            for (var i = 0; i < 3; i++)
            {
                if (TryGetDensityValue(rgbArray[i], out var value))
                {
                    rgbValues[i] = value * multiplier;
                }
                else
                {
                    allValid = false;
                    break;
                }
            }

            if (!allValid)
                return false;

            // Find max value
            var maxRgb = rgbValues.Max();

            // Scale down proportionally if any exceeds 1.0
            if (maxRgb > 1.0)
            {
                var scaleFactor = 1.0 / maxRgb;
                for (var i = 0; i < 3; i++)
                {
                    rgbValues[i] *= scaleFactor;
                }
            }

            // Apply the values
            for (var i = 0; i < 3; i++)
            {
                rgbArray[i] = Math.Round(rgbValues[i], 7);
            }

            return true;
        }

        bool ProcessDensitySection(JObject densityParent, string sectionName, bool makeUniform)
        {
            var section = densityParent.SelectToken(sectionName) as JObject;
            if (section == null)
                return false;

            var sectionModified = false;

            if (makeUniform)
            {
                var uniformToken = section.SelectToken("uniform");
                var maxDensityHeightToken = section.SelectToken("max_density_height");
                var zeroDensityHeightToken = section.SelectToken("zero_density_height");

                var isHeightBased = (maxDensityHeightToken != null || zeroDensityHeightToken != null) &&
                                   (uniformToken == null || !uniformToken.Value<bool>());

                if (isHeightBased)
                {
                    section.Remove("max_density_height");
                    section.Remove("zero_density_height");
                    section["uniform"] = true;
                    sectionModified = true;
                }
            }

            return sectionModified;
        }

        // Safely extract density values
        bool TryGetDensityValue(JToken token, out double value)
        {
            value = 0.0;

            switch (token.Type)
            {
                case JTokenType.Float:
                case JTokenType.Integer:
                    value = token.Value<double>();
                    return true;
                case JTokenType.String:
                    return double.TryParse(token.Value<string>(), out value);
                default:
                    return false;
            }
        }

        // Calculate new density with special handling for near-zero values
        double CalculateNewDensity(double currentDensity, double fogMultiplier)
        {
            const double tolerance = 0.0001;

            // If current density is effectively zero
            if (Math.Abs(currentDensity) < tolerance)
            {
                if (fogMultiplier <= 1.0)
                {
                    // Use multiplier as literal value (0.0 to 1.0)
                    return Math.Clamp(fogMultiplier, 0.0, 1.0);
                }
                else
                {
                    // Divide by 10 and use as literal value (1.0+ becomes 0.1+)
                    return Math.Clamp(fogMultiplier / 10.0, 0.0, 1.0);
                }
            }
            else
            {
                // Regular multiplication happens for non-zero values
                return Math.Clamp(currentDensity * fogMultiplier, 0.0, 1.0);
            }
        }
    }



    private static void ProcessEmissivity(PackInfo pack)
    {
        if (string.IsNullOrEmpty(pack.Path) || !Directory.Exists(pack.Path))
            return;

        var files = TextureSetHelper.RetrieveFilesFromTextureSets(pack.Path, TextureSetHelper.TextureType.Mer);

        if (!files.Any())
        {
            MainWindow.Log($"{pack.Name}: no MERS texture files found from texture sets.");
            return;
        }

        var userMult = EmissivityMultiplier;
        foreach (var file in files)
        {
            try
            {
                using var bmp = ReadImage(file, false);
                var width = bmp.Width;
                var height = bmp.Height;
                var wroteBack = false;

                // First pass: missivity processing
                if (userMult != 1.0)
                {
                    // Max green value within image
                    var maxGreen = 0;
                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            int g = bmp.GetPixel(x, y).G;
                            if (g > maxGreen) maxGreen = g;
                        }
                    }

                    // Only process if there are green pixels
                    if (maxGreen > 0)
                    {
                        // Excess Multiplier Dampner
                        var ratio = 255.0 / maxGreen;
                        var neededMult = ratio;
                        var effectiveMult = userMult < neededMult ? userMult : neededMult;
                        var excess = Math.Max(0, userMult - effectiveMult);

                        // Process existing emissivity
                        for (var y = 0; y < height; y++)
                        {
                            for (var x = 0; x < width; x++)
                            {
                                var origColor = bmp.GetPixel(x, y);
                                int origG = origColor.G;
                                if (origG == 0)
                                    continue;
                                // Multiply all by "effective" or necessary portion of the multiplier
                                var newG = origG * effectiveMult;
                                // Apply excess of multiplier to the rest at % effectiveness to partially preserve color composition
                                if (excess > 0)
                                {
                                    newG += origG * excess * EMISSIVE_EXCESS_INTENSITY_DAMPEN;
                                }

                                // Custom rounding logic: if < 127.5, round up; if >= 127.5, round down
                                int finalG;
                                if (newG < 127.5)
                                {
                                    finalG = (int)Math.Ceiling(newG);
                                }
                                else
                                {
                                    finalG = (int)Math.Floor(newG);
                                }

                                finalG = Math.Clamp(finalG, 0, 255);

                                if (finalG != origG)
                                {
                                    wroteBack = true;
                                    var newColor = Color.FromArgb(origColor.A, origColor.R, finalG, origColor.B);
                                    bmp.SetPixel(x, y, newColor);
                                }
                            }
                        }
                    }
                }

                // Second pass: Add ambient light to all pixels
                if (AddEmissivityAmbientLight)
                {
                    // Determine & apply ambient light amount (Multiplier rounded up, plus one)
                    var ambientAmount = (int)Math.Ceiling(userMult) + 1;

                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            var origColor = bmp.GetPixel(x, y);
                            var newG = Math.Clamp(origColor.G + ambientAmount, 0, 255);

                            if (newG != origColor.G)
                            {
                                wroteBack = true;
                                var newColor = Color.FromArgb(origColor.A, origColor.R, newG, origColor.B);
                                bmp.SetPixel(x, y, newColor);
                            }
                        }
                    }
                }

                if (wroteBack)
                {
                    WriteImageAsTGA(bmp, file);
                    // MainWindow.Log($"{packName}: updated emissivity in {Path.GetFileName(file)}.");
                }
                else
                {
                    // MainWindow.Log($"{packName}: no emissivity changes in {Path.GetFileName(file)}.");
                }
            }
            catch (Exception ex)
            {
                MainWindow.Log($"{pack.Name}: error processing {Path.GetFileName(file)} — {ex.Message}");
                // Updates UI which can cause freezing if too many files give error, but it is worth it as logs will appear in the end
            }
        }
    }



    private static void ProcessNormalIntensity(PackInfo pack)
    {
        if (string.IsNullOrEmpty(pack.Path) || !Directory.Exists(pack.Path))
            return;

        // Get normal and heightmap files from texture sets
        var allNormalFiles = TextureSetHelper.RetrieveFilesFromTextureSets(pack.Path, TextureSetHelper.TextureType.Normal);
        var allHeightmapFiles = TextureSetHelper.RetrieveFilesFromTextureSets(pack.Path, TextureSetHelper.TextureType.Heightmap);

        // Check if we have anything to process at all
        if (!allNormalFiles.Any() && !allHeightmapFiles.Any())
        {
            MainWindow.Log($"{pack.Name}: no normal or heightmap texture files found from texture sets.", MainWindow.LogLevel.Warning);
            return;
        }

        // Process heightmaps first
        ProcessHeightmapsIntensity(allHeightmapFiles);

        var files = new List<string>();

        foreach (var file in allNormalFiles)
        {
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(file);
            var fileDir = Path.GetDirectoryName(file);

            // Check if a double-normal variant exists (some blocks already end with _normal suffix)
            var possibleExtensions = new[] { ".tga", ".png", ".jpg", ".jpeg" };
            string? doubleNormalPath = null;

            foreach (var ext in possibleExtensions)
            {
                var testPath = Path.Combine(fileDir, fileNameWithoutExt + "_normal" + ext);
                if (File.Exists(testPath))
                {
                    doubleNormalPath = testPath;
                    break;
                }
            }

            if (doubleNormalPath != null)
            {
                // Use the double-normal version (the real normal map)
                if (!files.Contains(doubleNormalPath))
                    files.Add(doubleNormalPath);
            }
            else
            {
                // No double-normal exists, so this normal file is the actual normal map
                files.Add(file);
            }
        }

        if (!files.Any())
        {
            // No normal files to process, but that's okay if we had heightmaps
            // MainWindow.Log($"{pack.Name}: no processable normal files found.");
            return;
        }

        var intensityPercent = NormalIntensity / 100.0; // percentage -> multiplier

        foreach (var file in files)
        {
            try
            {
                using var bmp = ReadImage(file, false);
                var width = bmp.Width;
                var height = bmp.Height;
                bool wroteBack = false;

                // For reduction (intensity < 1.0), simple linear scaling toward neutral
                if (intensityPercent <= 1.0)
                {
                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            var origColor = bmp.GetPixel(x, y);

                            // Simple linear interpolation toward 128 (neutral)
                            var newR = (int)Math.Round(128 + (origColor.R - 128) * intensityPercent);
                            var newG = (int)Math.Round(128 + (origColor.G - 128) * intensityPercent);

                            newR = Math.Clamp(newR, 0, 255);
                            newG = Math.Clamp(newG, 0, 255);

                            if (newR != origColor.R || newG != origColor.G)
                            {
                                wroteBack = true;
                                var newColor = Color.FromArgb(origColor.A, newR, newG, origColor.B);
                                bmp.SetPixel(x, y, newColor);
                            }
                        }
                    }
                }
                else
                {
                    // For increase (intensity > 1.0), use proportional compression approach

                    // Find the maximum deviation that would occur after scaling
                    double maxIdealDeviation = 0;
                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            var pixel = bmp.GetPixel(x, y);
                            var rDev = Math.Abs((pixel.R - 128.0) * intensityPercent);
                            var gDev = Math.Abs((pixel.G - 128.0) * intensityPercent);
                            var maxDev = Math.Max(rDev, gDev);
                            if (maxDev > maxIdealDeviation)
                                maxIdealDeviation = maxDev;
                        }
                    }

                    if (maxIdealDeviation == 0)
                    {
                        // Flat normal map, nothing to do
                        continue;
                    }

                    // Calculate compression ratio to fit within valid range
                    var compressionRatio = maxIdealDeviation > 127.0 ? 127.0 / maxIdealDeviation : 1.0;

                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            var origColor = bmp.GetPixel(x, y);

                            // Apply intensity then compression
                            var idealR = 128.0 + (origColor.R - 128.0) * intensityPercent * compressionRatio;
                            var idealG = 128.0 + (origColor.G - 128.0) * intensityPercent * compressionRatio;

                            var newR = (int)Math.Round(idealR);
                            var newG = (int)Math.Round(idealG);

                            newR = Math.Clamp(newR, 0, 255);
                            newG = Math.Clamp(newG, 0, 255);

                            if (newR != origColor.R || newG != origColor.G)
                            {
                                wroteBack = true;
                                var newColor = Color.FromArgb(origColor.A, newR, newG, origColor.B);
                                bmp.SetPixel(x, y, newColor);
                            }
                        }
                    }
                }

                if (wroteBack)
                {
                    WriteImageAsTGA(bmp, file);
                    // MainWindow.Log($"{packName}: updated normal intensity in {Path.GetFileName(file)}.");
                }
                else
                {
                    // MainWindow.Log($"{packName}: no normal intensity changes in {Path.GetFileName(file)}.");
                }
            }
            catch (Exception ex)
            {
                // MainWindow.Log($"{packName}: error processing {Path.GetFileName(file)} — {ex.Message}");
            }
        }

        // Detail-preserving heightmap contrast adjustment
        void ProcessHeightmapsIntensity(string[] heightmapFiles)
        {
            var userIntensity = NormalIntensity / 100.0;

            foreach (var file in heightmapFiles)
            {
                try
                {
                    using var bmp = ReadImage(file, false);
                    var width = bmp.Width;
                    var height = bmp.Height;

                    // Find min/max values in the heightmap
                    int minGray = 255, maxGray = 0;
                    for (var y = 0; y < height; y++)
                        for (var x = 0; x < width; x++)
                        {
                            var gray = bmp.GetPixel(x, y).R;
                            if (gray < minGray) minGray = gray;
                            if (gray > maxGray) maxGray = gray;
                        }

                    double currentSpan = maxGray - minGray;
                    if (currentSpan == 0)
                    {
                        // Flat image, nothing to do
                        continue;
                    }

                    // Calculate the ideal new span based on user intensity
                    var idealSpan = currentSpan * userIntensity;

                    // Determine the actual span we can achieve (clamped to 255)
                    var actualSpan = Math.Min(idealSpan, 255.0);

                    // Calculate the center point for the transformation
                    var currentCenter = (minGray + maxGray) / 2.0;
                    var newCenter = 127.5; // Target center of 0-255 range

                    // If ideal span exceeds 255, we need to compress proportionally
                    var compressionRatio = actualSpan / Math.Max(idealSpan, actualSpan);

                    bool hasChanges = false;

                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            var origColor = bmp.GetPixel(x, y);
                            var gray = origColor.R;

                            // Calculate deviation from current center
                            var deviation = gray - currentCenter;

                            // Apply intensity scaling with compression if needed
                            var newDeviation = deviation * userIntensity * compressionRatio;

                            // Calculate final value around new center
                            var newGray = newCenter + newDeviation;

                            // Clamp and round
                            var finalGray = (int)Math.Round(newGray);
                            finalGray = Math.Clamp(finalGray, 0, 255);

                            if (finalGray != gray)
                            {
                                hasChanges = true;
                                var newColor = Color.FromArgb(origColor.A, finalGray, finalGray, finalGray);
                                bmp.SetPixel(x, y, newColor);
                            }
                        }
                    }

                    if (hasChanges)
                    {
                        WriteImageAsTGA(bmp, file);
                    }
                }
                catch (Exception ex)
                {
                    // MainWindow.Log($"{pack.Name}: error processing heightmap {Path.GetFileName(file)} — {ex.Message}");
                }
            }
        }
    }



    private static void ProcessHeightmaps(PackInfo pack)
    {
        if (string.IsNullOrEmpty(pack.Path) || !Directory.Exists(pack.Path))
            return;

        // Get paired color and heightmap textures from texture sets
        var texturePairs = TextureSetHelper.RetrieveTextureSetPairs(pack.Path, TextureSetHelper.TextureType.Color, TextureSetHelper.TextureType.Heightmap);

        if (!texturePairs.Any())
        {
            MainWindow.Log($"{pack.Name}: no texture sets with color and heightmap found.", MainWindow.LogLevel.Warning);
            return;
        }

        var alpha = ButcheredHeightmapAlpha;

        foreach (var (colormapFile, heightmapFile) in texturePairs)
        {
            // Skip if heightmap is missing (we need both for this process)
            if (string.IsNullOrEmpty(heightmapFile))
            {
                MainWindow.Log($"{pack.Name}: heightmap not found for {Path.GetFileName(colormapFile)}; skipped.", MainWindow.LogLevel.Warning);
                continue;
            }

            try
            {
                using var heightmapBmp = ReadImage(heightmapFile, false);
                using var colormapBmp = ReadImage(colormapFile, false);

                var width = heightmapBmp.Width;
                var height = heightmapBmp.Height;

                // Ensure dimensions match
                if (colormapBmp.Width != width || colormapBmp.Height != height)
                {
                    MainWindow.Log($"{pack.Name}: dimension mismatch between heightmap and colormap for {Path.GetFileName(heightmapFile)}; skipped.", MainWindow.LogLevel.Warning);
                    continue;
                }

                // 1: Convert colormap to greyscale and find min/max values
                var greyscaleValues = new byte[width, height];
                byte minValue = 255;
                byte maxValue = 0;

                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var color = colormapBmp.GetPixel(x, y);
                        // Standard greyscale conversion
                        var greyValue = (byte)(0.299 * color.R + 0.587 * color.G + 0.114 * color.B);
                        greyscaleValues[x, y] = greyValue;

                        if (greyValue < minValue) minValue = greyValue;
                        if (greyValue > maxValue) maxValue = greyValue;
                    }
                }

                // 2: Stretch/Maximize
                var stretchedValues = new byte[width, height];
                double range = maxValue - minValue;

                if (range == 0)
                {
                    // All pixels are the same value
                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            stretchedValues[x, y] = 128;
                        }
                    }
                }
                else
                {
                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            var normalized = (greyscaleValues[x, y] - minValue) / range;
                            stretchedValues[x, y] = (byte)(normalized * 255);
                        }
                    }
                }

                // Step 3: Overlay stretched heightmap on original heightmap
                var wroteBack = false;
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var origColor = heightmapBmp.GetPixel(x, y);
                        var newHeightValue = stretchedValues[x, y];

                        // Alpha blend
                        var blendedValue = (alpha * newHeightValue + (255 - alpha) * origColor.R) / 255;
                        var finalValue = (byte)Math.Clamp(blendedValue, 0, 255);

                        if (finalValue != origColor.R)
                        {
                            wroteBack = true;
                            var newColor = Color.FromArgb(origColor.A, finalValue, finalValue, finalValue);
                            heightmapBmp.SetPixel(x, y, newColor);
                        }
                    }
                }

                if (wroteBack)
                {
                    WriteImageAsTGA(heightmapBmp, heightmapFile);
                    Debug.WriteLine($"{pack.Name}: updated heightmap in {Path.GetFileName(heightmapFile)}.");
                }
                else
                {
                    Debug.WriteLine($"{pack.Name}: no heightmap changes in {Path.GetFileName(heightmapFile)}.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{pack.Name}: error processing {Path.GetFileName(heightmapFile)} — {ex.Message}");
            }
        }
    }



    private static void ProcessRoughingUp(PackInfo pack)
    {
        if (string.IsNullOrEmpty(pack.Path) || !Directory.Exists(pack.Path))
            return;

        var files = TextureSetHelper.RetrieveFilesFromTextureSets(pack.Path, TextureSetHelper.TextureType.Mer);

        if (!files.Any())
        {
            MainWindow.Log($"{pack.Name}: no MERS texture files found from texture sets.");
            return;
        }

        var amount = RoughenUpIntensity;
        if (amount <= 0)
            return;

        foreach (var file in files)
        {
            try
            {
                using var bmp = ReadImage(file, false);
                var width = bmp.Width;
                var height = bmp.Height;

                var wroteBack = false;
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var origColor = bmp.GetPixel(x, y);
                        int origB = origColor.B;

                        // a curve where low values get high boost, high values get minimal boost
                        var normalized = origB / 255.0; // 0 to 1
                        var power = 3.0; // Higher power = a more aggressive curve
                        var inverseFactor = 1.0 - Math.Pow(normalized, power);

                        // At intensity 10: value 1 gets ~+20, value 40 gets ~+18, value 128 gets ~+5, value 190 gets ~+1
                        var maxBoost = 20.0;
                        var boost = amount / 10.0 * maxBoost * inverseFactor;

                        var newB = origB + boost;
                        var finalB = (int)Math.Round(newB);
                        finalB = Math.Clamp(finalB, 0, 255);

                        if (finalB != origB)
                        {
                            wroteBack = true;
                            var newColor = Color.FromArgb(origColor.A, origColor.R, origColor.G, finalB);
                            bmp.SetPixel(x, y, newColor);
                        }
                    }
                }

                if (wroteBack)
                {
                    WriteImageAsTGA(bmp, file);
                    // MainWindow.Log($"{packName}: updated roughness in {Path.GetFileName(file)}.");
                }
                else
                {
                    // MainWindow.Log($"{packName}: no roughness changes in {Path.GetFileName(file)}.");
                }
            }
            catch (Exception ex)
            {
                MainWindow.Log($"{pack.Name}: error processing {Path.GetFileName(file)} — {ex.Message}", MainWindow.LogLevel.Error);
                // Updates UI which can cause freezing if too many files give error, but it is worth it as logs will appear in the end
            }
        }
    }



    private static void ProcessMaterialNoise(PackInfo pack)
    {
        double CalculateEffectiveness(int colorValue)
        {
            if (colorValue == 128)
                return 1.0; // 100% effectiveness at 128

            if (colorValue < 128)
            {
                // Linear fall-off from 128 to 0: 100% at 128, 0% at 0
                return colorValue / 128.0;
            }
            else
            {
                // Linear fall-off from 128 to 255: 100% at 128, 33% at 255
                return 1.0 - (colorValue - 128) * 0.67 / 127.0;
            }
        }

        if (string.IsNullOrEmpty(pack.Path) || !Directory.Exists(pack.Path))
            return;

        var files = TextureSetHelper.RetrieveFilesFromTextureSets(pack.Path, TextureSetHelper.TextureType.Mer);

        if (!files.Any())
        {
            MainWindow.Log($"{pack.Name}: no MER(S) texture files found from texture sets.");
            return;
        }

        var materialNoiseOffset = MaterialNoiseOffset;
        if (materialNoiseOffset <= 0)
            return;

        var random = new Random();

        foreach (var file in files)
        {
            try
            {
                using var bmp = ReadImage(file, false);
                var width = bmp.Width;
                var height = bmp.Height;

                // Check if this is an animated texture (flipbook)
                var isAnimated = false;
                var frameHeight = width; // First frame is always square
                var frameCount = 1;

                if (width > 0 && height >= width * 2 && height % width == 0)
                {
                    frameCount = height / width;
                    isAnimated = frameCount >= 2; // Any number of frames 2+
                }
                else if (width == 0)
                {
                    continue; // Skip this image if width is 0
                }

                // Storage for noise offsets to apply to subsequent frames
                int[,] redOffsets = null;
                int[,] greenOffsets = null;
                int[,] blueOffsets = null;

                if (isAnimated)
                {
                    redOffsets = new int[width, frameHeight];
                    greenOffsets = new int[width, frameHeight];
                    blueOffsets = new int[width, frameHeight];
                }

                var wroteBack = false;

                for (var frame = 0; frame < frameCount; frame++)
                {
                    var frameStartY = frame * frameHeight;

                    for (var y = 0; y < frameHeight; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            var actualY = frameStartY + y;
                            var origColor = bmp.GetPixel(x, actualY);
                            int r = origColor.R;
                            int g = origColor.G;
                            int b = origColor.B;

                            int redOffset, greenOffset, blueOffset;

                            if (isAnimated && frame == 0)
                            {
                                // First frame: generate and store noise offsets
                                redOffset = random.Next(-materialNoiseOffset, materialNoiseOffset + 1);
                                greenOffset = random.Next(-materialNoiseOffset, materialNoiseOffset + 1);
                                blueOffset = random.Next(-materialNoiseOffset, materialNoiseOffset + 1);

                                redOffsets[x, y] = redOffset;
                                greenOffsets[x, y] = greenOffset;
                                blueOffsets[x, y] = blueOffset;
                            }
                            else if (isAnimated && frame > 0)
                            {
                                // Subsequent frames: use stored offsets
                                redOffset = redOffsets[x, y];
                                greenOffset = greenOffsets[x, y];
                                blueOffset = blueOffsets[x, y];
                            }
                            else
                            {
                                // Non-animated texture: generate unique offsets as before
                                redOffset = random.Next(-materialNoiseOffset, materialNoiseOffset + 1);
                                greenOffset = random.Next(-materialNoiseOffset, materialNoiseOffset + 1);
                                blueOffset = random.Next(-materialNoiseOffset, materialNoiseOffset + 1);
                            }

                            // effectiveness based on current color values
                            var redEffectiveness = CalculateEffectiveness(r);
                            var greenEffectiveness = CalculateEffectiveness(g) * 0.25; // keep green at 1/4 effectiveness, no one likes grainy emissives
                            var blueEffectiveness = CalculateEffectiveness(b);

                            // set effectiveness of offsets, rounded
                            var effectiveRedOffset = (int)Math.Round(redOffset * redEffectiveness);
                            var effectiveGreenOffset = (int)Math.Round(greenOffset * greenEffectiveness);
                            var effectiveBlueOffset = (int)Math.Round(blueOffset * blueEffectiveness);

                            var newR = r + effectiveRedOffset;
                            var newG = g + effectiveGreenOffset;
                            var newB = b + effectiveBlueOffset;

                            // anti-clipping rule: discard if would cause clipping, keep original colors!
                            if (newR < 0 || newR > 255) newR = r;
                            if (newG < 0 || newG > 255) newG = g;
                            if (newB < 0 || newB > 255) newB = b;

                            if (newR != r || newG != g || newB != b)
                            {
                                wroteBack = true;
                                var newColor = Color.FromArgb(origColor.A, newR, newG, newB);
                                bmp.SetPixel(x, actualY, newColor);
                            }
                        }
                    }
                }

                if (wroteBack)
                {
                    WriteImageAsTGA(bmp, file);
                    // MainWindow.Log($"{packName}: added material noise to {Path.GetFileName(file)}.");
                }
                else
                {
                    // MainWindow.Log($"{packName}: no changes in material noise for {Path.GetFileName(file)}.");
                }
            }
            catch (Exception ex)
            {
                MainWindow.Log($"{pack.Name}: error processing {Path.GetFileName(file)} — {ex.Message}", MainWindow.LogLevel.Error);
                // Updates UI which can cause freezing if too many files give error, but it is worth it as logs will appear in the end
            }
        }
    }


    // This one is a copy of the above with something extra to keep the same noise pattern across texture variants
    // e.g. on/off etc... but I'm not sure about it yet, or if it is even worth it.
    // The whole idea is that we got a bunch of words, we detect variations based on texture names
    private static void ProcessMaterialGrain(PackInfo pack)
    {
        double CalculateEffectiveness(int colorValue)
        {
            if (colorValue == 128)
                return 1.0; // 100% effectiveness at 128

            if (colorValue < 128)
            {
                // Linear fall-off from 128 to 0: 100% at 128, 0% at 0
                return colorValue / 128.0;
            }
            else
            {
                // Linear fall-off from 128 to 255: 100% at 128, 33% at 255
                return 1.0 - (colorValue - 128) * 0.67 / 127.0;
            }
        }

        string GetBaseFilename(string filePath)
        {
            var filename = Path.GetFileNameWithoutExtension(filePath);

            // Define all known variant suffixes (case insensitive)
            var variantSuffixes = new[]
            {
            "_on", "_off", "_active", "_inactive", "_dormant", "_bloom", "_ejecting",
            "_lit", "_unlit", "_powered", "_crafting"
        };

            // Split by underscores and rebuild without any variant suffixes
            var parts = filename.Split('_');
            var baseParts = new List<string>();

            foreach (var part in parts)
            {
                var isVariantSuffix = false;
                foreach (var suffix in variantSuffixes)
                {
                    // Remove the leading underscore from suffix for comparison
                    var suffixWithoutUnderscore = suffix.Substring(1);
                    if (part.Equals(suffixWithoutUnderscore, StringComparison.OrdinalIgnoreCase))
                    {
                        isVariantSuffix = true;
                        break;
                    }
                }

                if (!isVariantSuffix)
                {
                    baseParts.Add(part);
                }
            }

            return string.Join("_", baseParts);
        }

        if (!pack.Enabled || string.IsNullOrEmpty(pack.Path) || !Directory.Exists(pack.Path))
            return;

        var files = Directory.GetFiles(pack.Path, "*_mer.tga", SearchOption.AllDirectories);
        if (!files.Any())
        {
            MainWindow.Log($"{pack.Name}: no _mer.tga files found.", MainWindow.LogLevel.Warning);
            return;
        }

        var materialNoiseOffset = MaterialNoiseOffset;
        if (materialNoiseOffset <= 0)
            return;

        var random = new Random();

        // Cache for shared noise patterns between variants and flipbook frames
        var noisePatternCache = new Dictionary<string, (int[,] red, int[,] green, int[,] blue)>();

        // Group files by their base filename to identify variant families
        var fileGroups = files.GroupBy(file => GetBaseFilename(file))
                              .ToDictionary(g => g.Key, g => g.ToList());

        // Track processed files to avoid double-processing
        var processedFiles = new HashSet<string>();

        foreach (var fileGroup in fileGroups)
        {
            var baseFilename = fileGroup.Key;
            var variantFiles = fileGroup.Value;
            var hasMultipleVariants = variantFiles.Count > 1;

            foreach (var file in variantFiles)
            {
                if (processedFiles.Contains(file))
                    continue;

                processedFiles.Add(file);

                try
                {
                    using var bmp = ReadImage(file, false);
                    var width = bmp.Width;
                    var height = bmp.Height;

                    // Check if this is an animated texture (flipbook)
                    var isAnimated = false;
                    var frameHeight = width; // First frame is always square
                    var frameCount = 1;

                    if (width > 0 && height >= width * 2 && height % width == 0)
                    {
                        frameCount = height / width;
                        isAnimated = frameCount >= 2; // Any number of frames 2+
                    }
                    else if (width == 0)
                    {
                        continue; // Skip this image if width is 0
                    }

                    // Choose cache key strategy based on whether variants exist
                    string cacheKey;
                    if (hasMultipleVariants)
                    {
                        // Use base filename for variants to share noise patterns
                        cacheKey = $"{baseFilename}_{width}x{frameHeight}";
                    }
                    else
                    {
                        // Use full filename for standalone textures to avoid interference
                        cacheKey = $"{Path.GetFileNameWithoutExtension(file)}_{width}x{frameHeight}";
                    }

                    // Try to get existing noise pattern from cache
                    int[,] redOffsets = null;
                    int[,] greenOffsets = null;
                    int[,] blueOffsets = null;

                    if (noisePatternCache.TryGetValue(cacheKey, out var cachedPattern))
                    {
                        redOffsets = cachedPattern.red;
                        greenOffsets = cachedPattern.green;
                        blueOffsets = cachedPattern.blue;
                    }
                    else
                    {
                        // Generate new noise pattern and cache it
                        redOffsets = new int[width, frameHeight];
                        greenOffsets = new int[width, frameHeight];
                        blueOffsets = new int[width, frameHeight];

                        // Pre-generate noise pattern for the first frame dimensions
                        for (var y = 0; y < frameHeight; y++)
                        {
                            for (var x = 0; x < width; x++)
                            {
                                redOffsets[x, y] = random.Next(-materialNoiseOffset, materialNoiseOffset + 1);
                                greenOffsets[x, y] = random.Next(-materialNoiseOffset, materialNoiseOffset + 1);
                                blueOffsets[x, y] = random.Next(-materialNoiseOffset, materialNoiseOffset + 1);
                            }
                        }

                        noisePatternCache[cacheKey] = (redOffsets, greenOffsets, blueOffsets);
                    }

                    var wroteBack = false;

                    for (var frame = 0; frame < frameCount; frame++)
                    {
                        var frameStartY = frame * frameHeight;

                        for (var y = 0; y < frameHeight; y++)
                        {
                            for (var x = 0; x < width; x++)
                            {
                                var actualY = frameStartY + y;
                                var origColor = bmp.GetPixel(x, actualY);
                                int r = origColor.R;
                                int g = origColor.G;
                                int b = origColor.B;

                                // Use cached noise offsets (same for all frames and variants)
                                var redOffset = redOffsets[x, y];
                                var greenOffset = greenOffsets[x, y];
                                var blueOffset = blueOffsets[x, y];

                                // effectiveness based on current color values
                                var redEffectiveness = CalculateEffectiveness(r);
                                var greenEffectiveness = CalculateEffectiveness(g) * 0.25; // keep green at 1/4 effectiveness, no one likes grainy emissives
                                var blueEffectiveness = CalculateEffectiveness(b);

                                // set effectiveness of offsets, rounded
                                var effectiveRedOffset = (int)Math.Round(redOffset * redEffectiveness);
                                var effectiveGreenOffset = (int)Math.Round(greenOffset * greenEffectiveness);
                                var effectiveBlueOffset = (int)Math.Round(blueOffset * blueEffectiveness);

                                var newR = r + effectiveRedOffset;
                                var newG = g + effectiveGreenOffset;
                                var newB = b + effectiveBlueOffset;

                                // anti-clipping rule: discard if would cause clipping, keep original colors!
                                if (newR < 0 || newR > 255) newR = r;
                                if (newG < 0 || newG > 255) newG = g;
                                if (newB < 0 || newB > 255) newB = b;

                                if (newR != r || newG != g || newB != b)
                                {
                                    wroteBack = true;
                                    var newColor = Color.FromArgb(origColor.A, newR, newG, newB);
                                    bmp.SetPixel(x, actualY, newColor);
                                }
                            }
                        }
                    }

                    if (wroteBack)
                    {
                        WriteImageAsTGA(bmp, file);
                        // MainWindow.Log($"{packName}: added material noise to {Path.GetFileName(file)}.");
                    }
                    else
                    {
                        // MainWindow.Log($"{packName}: no changes in material noise for {Path.GetFileName(file)}.");
                    }
                }
                catch (Exception ex)
                {
                    MainWindow.Log($"{pack.Name}: error processing {Path.GetFileName(file)} — {ex.Message}", MainWindow.LogLevel.Error);
                    // Updates UI which can cause freezing if too many files give error, but it is worth it as logs will appear in the end
                }
            }
        }
    }

    #endregion Processors -------------------
}



public static class TextureSetHelper
{
    public enum TextureType
    {
        Color,
        Mer,        // metalness_emissive_roughness or metalness_emissive_roughness_subsurface
        Normal,
        Heightmap
    }

    private static readonly string[] SupportedExtensions = { ".tga", ".png", ".jpg", ".jpeg" };

    /// <summary>
    /// Retrieves paired texture file paths from texture set JSONs.
    /// </summary>
    /// <param name="rootPath">Folder to search for texture set JSONs.</param>
    /// <param name="primaryType">Primary texture type (e.g., Color).</param>
    /// <param name="secondaryType">Secondary texture type (e.g., Heightmap).</param>
    /// <returns>Array of texture pairs. Secondary can be null if not found.</returns>
    public static (string primary, string? secondary)[] RetrieveTextureSetPairs(string rootPath, TextureType primaryType, TextureType secondaryType)
    {
        if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
            return Array.Empty<(string, string?)>();

        var jsonFiles = Directory.GetFiles(rootPath, "*.texture_set.json", SearchOption.AllDirectories);
        var foundPairs = new List<(string primary, string? secondary)>();

        foreach (var jsonFile in jsonFiles)
        {
            try
            {
                var text = File.ReadAllText(jsonFile);
                var root = JObject.Parse(text);
                if (root.SelectToken("minecraft:texture_set") is not JObject set)
                    continue;

                string? primaryTextureName = primaryType switch
                {
                    TextureType.Color => set.Value<string>("color"),
                    TextureType.Mer => set.Value<string>("metalness_emissive_roughness") ?? set.Value<string>("metalness_emissive_roughness_subsurface"),
                    TextureType.Normal => set.Value<string>("normal"),
                    TextureType.Heightmap => set.Value<string>("heightmap"),
                    _ => null
                };

                string? secondaryTextureName = secondaryType switch
                {
                    TextureType.Color => set.Value<string>("color"),
                    TextureType.Mer => set.Value<string>("metalness_emissive_roughness") ?? set.Value<string>("metalness_emissive_roughness_subsurface"),
                    TextureType.Normal => set.Value<string>("normal"),
                    TextureType.Heightmap => set.Value<string>("heightmap"),
                    _ => null
                };

                if (string.IsNullOrEmpty(primaryTextureName))
                    continue;

                var folder = Path.GetDirectoryName(jsonFile);
                var primaryFound = FindTextureFile(folder, primaryTextureName);

                if (!string.IsNullOrEmpty(primaryFound))
                {
                    string? secondaryFound = null;
                    if (!string.IsNullOrEmpty(secondaryTextureName))
                    {
                        secondaryFound = FindTextureFile(folder, secondaryTextureName);
                    }

                    foundPairs.Add((primaryFound, secondaryFound));
                }
            }
            catch
            {
                // Ignore malformed JSONs or IO errors
            }
        }

        return foundPairs.ToArray();
    }

    /// <summary>
    /// Retrieves texture file paths referenced by texture set JSONs in the given folder.
    /// </summary>
    /// <param name="rootPath">Folder to search for texture set JSONs.</param>
    /// <param name="type">Texture type to retrieve.</param>
    /// <returns>Array of found texture file paths (unique entries only).</returns>
    public static string[] RetrieveFilesFromTextureSets(string rootPath, TextureType type)
    {
        if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
            return Array.Empty<string>();

        var jsonFiles = Directory.GetFiles(rootPath, "*.texture_set.json", SearchOption.AllDirectories);
        var foundFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var jsonFile in jsonFiles)
        {
            try
            {
                var text = File.ReadAllText(jsonFile);
                var root = JObject.Parse(text);
                if (root.SelectToken("minecraft:texture_set") is not JObject set)
                    continue;

                string? textureName = type switch
                {
                    TextureType.Color => set.Value<string>("color"),
                    TextureType.Mer => set.Value<string>("metalness_emissive_roughness") ?? set.Value<string>("metalness_emissive_roughness_subsurface"),
                    TextureType.Normal => set.Value<string>("normal"),
                    TextureType.Heightmap => set.Value<string>("heightmap"),
                    _ => null
                };

                if (string.IsNullOrEmpty(textureName))
                    continue;

                var folder = Path.GetDirectoryName(jsonFile);
                var found = FindTextureFile(folder, textureName);

                if (!string.IsNullOrEmpty(found))
                    foundFiles.Add(found);
            }
            catch
            {
                // Ignore malformed JSONs or IO errors
            }
        }

        return foundFiles.ToArray();
    }

    /// <summary>
    /// Finds a texture file with case-insensitive search, trying extensions in priority order.
    /// </summary>
    /// <param name="folder">Directory to search in.</param>
    /// <param name="textureName">Base texture name without extension.</param>
    /// <returns>Full path to found file, or null if not found.</returns>
    private static string FindTextureFile(string folder, string textureName)
    {
        foreach (var ext in SupportedExtensions)
        {
            var targetPath = Path.Combine(folder, textureName + ext);

            // Try exact case first (fastest)
            if (File.Exists(targetPath))
                return targetPath;

            // If exact case fails, do case-insensitive search
            try
            {
                var files = Directory.GetFiles(folder, textureName + ext, SearchOption.TopDirectoryOnly);
                if (files.Length > 0)
                    return files[0];
            }
            catch
            {
                // Directory might not exist or access denied, continue to next extension
            }
        }

        return null;
    }
}