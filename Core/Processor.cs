using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using static Vanilla_RTX_Tuner_WinUI.TunerVariables;
using static Vanilla_RTX_Tuner_WinUI.Modules.Helpers;
using Vanilla_RTX_Tuner_WinUI.Core;
using static Vanilla_RTX_Tuner_WinUI.Core.ProcessorVariables;

namespace Vanilla_RTX_Tuner_WinUI.Core;

public static class ProcessorVariables
{
    public const bool FOG_UNIFORM_HEIGHT = false;
    public const double FOG_EXCESS_DENSITY_DAMPEN = 0.001;
    public const double FOG_EXCESS_DENSITY_TO_SCATTER_DAMPEN = 0.005;
    public const double FOG_EXCESS_SCATTERING_DAMPEN = 0.005;
    public const double EMISSIVE_EXCESS_INTENSITY_DAMPEN = 0.1;
    public const double NORMALMAP_EXCESS_INTENSITY_DAMPEN = 0.1;
}

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
        new PackInfo("Vanilla RTX", VanillaRTXLocation, IsVanillaRTXEnabled),               // 0
        new PackInfo("Vanilla RTX Normals", VanillaRTXNormalsLocation, IsNormalsEnabled),   // 1
        new PackInfo("Vanilla RTX Opus", VanillaRTXOpusLocation, IsOpusEnabled)             // 2
    };

        if (FogMultiplier != 1.0)
        {
            foreach (var p in packs) ProcessFog(p); // All
        }

        if (EmissivityMultiplier != 1.0 || AddEmissivityAmbientLight == true)
        {
            foreach (var p in packs) ProcessEmissivity(p); // All
        }

        if (NormalIntensity != 100)
        {
            foreach (var p in packs) ProcessNormalIntensity(p); // All
        }

        if (MaterialNoiseOffset != 0)
        {
            foreach (var p in packs) ProcessMaterialNoise(p); // All
        }

        if (RoughenUpIntensity != 0)
        {
            foreach (var p in packs) ProcessRoughingUp(p); // All
        }

        if (ButcheredHeightmapAlpha != 0)
        {
            ProcessHeightmaps(packs[0]); // Only Vanilla RTX
        }
    }


    // TODO: make processors return reasons of their failure for easier debugging at the end without touching UI thread directly.
    // Also make them log any unexpected oddities in Vanilla RTX (whether it be size, opacity, etc...) as warnings
    #region ------------------- Processors
    private static void ProcessFog(PackInfo pack)
    {


        if (!pack.Enabled || string.IsNullOrEmpty(pack.Path) || !Directory.Exists(pack.Path))
            return;

        var files = Directory.GetFiles(pack.Path, "*_volumetric_fog_setting.json", SearchOption.AllDirectories);
        if (!files.Any())
        {
            // MainWindow.Log($"{pack.Name}: no fog setting files found.");
            return;
        }

        foreach (var file in files)
        {
            try
            {
                var text = File.ReadAllText(file);
                var root = JObject.Parse(text);

                // volumetric density
                var fogSettings = root.SelectToken("minecraft:fog_settings") as JObject;
                var volumetric = fogSettings?.SelectToken("volumetric") as JObject;
                var density = volumetric?.SelectToken("density") as JObject;

                if (density == null)
                {
                    // MainWindow.Log($"{packName}: invalid structure in {Path.GetFileName(file)} - no density section found.");
                    continue;
                }

                var modified = false;

                var airSection = density.SelectToken("air") as JObject;
                var weatherSection = density.SelectToken("weather") as JObject;

                var nonZeroDensities = new List<(string name, JObject section, double currentDensity)>();
                var zeroDensities = new List<(string name, JObject section)>();

                // Check air density
                if (airSection != null)
                {
                    var airMaxDensityToken = airSection.SelectToken("max_density");
                    if (airMaxDensityToken != null && TryGetDensityValue(airMaxDensityToken, out var airDensity))
                    {
                        if (Math.Abs(airDensity) >= 0.0001) // Non-zero
                        {
                            nonZeroDensities.Add(("air", airSection, airDensity));
                        }
                        else
                        {
                            // Handle zero density with special logic
                            var newDensity = CalculateNewDensity(airDensity, FogMultiplier);
                            if (Math.Abs(newDensity - airDensity) >= 0.0000001)
                            {
                                airSection["max_density"] = Math.Round(newDensity, 7);
                                modified = true;
                            }
                            zeroDensities.Add(("air", airSection));
                        }
                    }
                }

                // Check weather density
                if (weatherSection != null)
                {
                    var weatherMaxDensityToken = weatherSection.SelectToken("max_density");
                    if (weatherMaxDensityToken != null && TryGetDensityValue(weatherMaxDensityToken, out var weatherDensity))
                    {
                        if (Math.Abs(weatherDensity) >= 0.0001) // Non-zero
                        {
                            nonZeroDensities.Add(("weather", weatherSection, weatherDensity));
                        }
                        else
                        {
                            // Handle zero density with special logic
                            var newDensity = CalculateNewDensity(weatherDensity, FogMultiplier);
                            if (Math.Abs(newDensity - weatherDensity) >= 0.0000001)
                            {
                                weatherSection["max_density"] = Math.Round(newDensity, 7);
                                modified = true;
                            }
                            zeroDensities.Add(("weather", weatherSection));
                        }
                    }
                }

                // Apply enhanced fog logic for non-zero densities
                if (nonZeroDensities.Any())
                {
                    var maxDensity = nonZeroDensities.Max(x => x.currentDensity);
                    double appliedMultiplier;
                    double excessMultiplier;

                    // Check if densities are already at or near maximum (from previous applications)
                    if (maxDensity >= 0.9999)
                    {
                        // All densities are already maxed, treat entire multiplier as excess
                        appliedMultiplier = 1.0;
                        excessMultiplier = FogMultiplier;
                    }
                    else
                    {
                        // Find the multiplier that would make the highest density reach exactly 1.0
                        var limitingMultiplier = 1.0 / maxDensity;
                        appliedMultiplier = Math.Min(FogMultiplier, limitingMultiplier);

                        // Apply base multiplication to all densities
                        foreach (var (name, section, currentDensity) in nonZeroDensities)
                        {
                            var newDensity = Math.Clamp(currentDensity * appliedMultiplier, 0.0, 1.0);
                            section["max_density"] = Math.Round(newDensity, 7);
                            modified = true;
                        }

                        excessMultiplier = FogMultiplier / appliedMultiplier;
                    }

                    // Handle excess if there is any
                    if (excessMultiplier > 1.0)
                    {
                        var excessIncrease = excessMultiplier - 1.0;

                        // Apply excess to densities that didn't reach 1.0 (only if we have multiple non-zero densities)
                        if (nonZeroDensities.Count > 1 && excessIncrease > 0)
                        {
                            foreach (var (name, section, currentDensity) in nonZeroDensities)
                            {
                                var currentValue = section["max_density"].Value<double>();
                                if (currentValue < 1.0)
                                {
                                    var dampenedIncrease = excessIncrease * FOG_EXCESS_DENSITY_DAMPEN;
                                    var newValue = Math.Clamp(currentValue + dampenedIncrease, 0.0, 1.0);
                                    section["max_density"] = Math.Round(newValue, 7);
                                    modified = true;
                                }
                            }
                        }

                        // Apply excess to scattering values
                        var mediaCoefficients = volumetric?.SelectToken("media_coefficients") as JObject;
                        var airCoefficients = mediaCoefficients?.SelectToken("air") as JObject;
                        if (airCoefficients != null)
                        {
                            var scatteringToken = airCoefficients.SelectToken("scattering") as JArray;
                            if (scatteringToken != null && scatteringToken.Count >= 3)
                            {
                                // Apply FOG_EXCESS_DENSITY_TO_SCATTER_DAMPEN to the excess before applying to scattering
                                var scatteringIncrease = excessIncrease * FOG_EXCESS_DENSITY_TO_SCATTER_DAMPEN;
                                var rgbValues = new double[3];
                                var canIncrease = new bool[3];

                                // Get current RGB values
                                for (int i = 0; i < 3; i++)
                                {
                                    if (TryGetDensityValue(scatteringToken[i], out var value))
                                    {
                                        rgbValues[i] = value;
                                        canIncrease[i] = true;
                                    }
                                }

                                // Apply initial scattering increase
                                var totalOverflow = 0.0;
                                var componentsToProcess = canIncrease.Count(x => x);

                                for (int i = 0; i < 3; i++)
                                {
                                    if (canIncrease[i])
                                    {
                                        var newValue = rgbValues[i] + scatteringIncrease;
                                        if (newValue > 1.0)
                                        {
                                            totalOverflow += (newValue - 1.0);
                                            rgbValues[i] = 1.0;
                                            canIncrease[i] = false;
                                            componentsToProcess--;
                                        }
                                        else
                                        {
                                            rgbValues[i] = newValue;
                                        }
                                    }
                                }

                                // Apply overflow with final DAMPEN to components that didn't max out
                                if (totalOverflow > 0 && componentsToProcess > 0)
                                {
                                    var dampenedOverflow = totalOverflow * FOG_EXCESS_SCATTERING_DAMPEN / componentsToProcess;
                                    for (int i = 0; i < 3; i++)
                                    {
                                        if (canIncrease[i])
                                        {
                                            rgbValues[i] = Math.Clamp(rgbValues[i] + dampenedOverflow, 0.0, 1.0);
                                        }
                                    }
                                }

                                // Update the scattering values
                                for (int i = 0; i < 3; i++)
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

                if (modified)
                {
                    File.WriteAllText(file, root.ToString(Newtonsoft.Json.Formatting.Indented));
                    // MainWindow.Log($"{packName}: updated fog densities in {Path.GetFileName(file)}.");
                }
                else
                {
                    // MainWindow.Log($"{packName}: no fog density values to update in {Path.GetFileName(file)}.");
                }
            }
            catch (Exception ex)
            {
                // MainWindow.Log($"{packName}: error processing {Path.GetFileName(file)} — {ex.Message}");
            }
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

        // safely extract density values
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

        // calculate new density with special handling of the multiplier depending on current density
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
                // regular multiplication happens for non-zero values
                return Math.Clamp(currentDensity * fogMultiplier, 0.0, 1.0);
            }
        }
    }



    private static void ProcessEmissivity(PackInfo pack)
    {
        if (!pack.Enabled || string.IsNullOrEmpty(pack.Path) || !Directory.Exists(pack.Path))
            return;
        var files = Directory.GetFiles(pack.Path, "*_mer.tga", SearchOption.AllDirectories);
        if (!files.Any())
        {
            MainWindow.Log($"{pack.Name}: no _mer.tgas files found.");
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


    // TODO: Force absolute perservation of contrast in Vanilla RTX heightmaps
    // Introduce a curve function that'd make it impossible for values super close to each other to blend in or become the same color
    // A kind of special heightmap processor
    private static void ProcessNormalIntensity(PackInfo pack)
    {
        if (!pack.Enabled || string.IsNullOrEmpty(pack.Path) || !Directory.Exists(pack.Path))
            return;

        var allNormalFiles = Directory.GetFiles(pack.Path, "*_normal.tga", SearchOption.AllDirectories);
        var files = new List<string>();

        // For Vanilla RTX
        var allHeightmapFiles = Directory.GetFiles(pack.Path, "*_heightmap.tga", SearchOption.AllDirectories);
        ProcessHeightmapsIntensity(allHeightmapFiles);

        foreach (var file in allNormalFiles)
        {
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(file);

            // Check if a double-normal variant exists (some blocks already end with _normal suffix)
            var doubleNormalPath = Path.Combine(Path.GetDirectoryName(file), fileNameWithoutExt + "_normal.tga");

            if (File.Exists(doubleNormalPath))
            {
                // Use the double-normal version (the real normal map)
                if (!files.Contains(doubleNormalPath))
                    files.Add(doubleNormalPath);
            }
            else
            {
                // No double-normal exists, so this _normal.tga is the actual normal map
                files.Add(file);
            }
        }

        if (!files.Any())
        {
            MainWindow.Log($"{pack.Name}: no _normal.tga files found.", MainWindow.LogLevel.Warning);
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

                // Find maximum deviation from 128 in R and G channels
                double maxDeviation = 0;
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var pixel = bmp.GetPixel(x, y);
                        var rDev = Math.Abs(pixel.R - 128.0);
                        var gDev = Math.Abs(pixel.G - 128.0);
                        var totalDev = Math.Max(rDev, gDev);
                        if (totalDev > maxDeviation)
                            maxDeviation = totalDev;
                    }
                }
                if (maxDeviation == 0)
                {
                    // MainWindow.Log($"{packName}: {Path.GetFileName(file)} has no normal data; skipped.");
                    continue;
                }

                // Calculate effective multiplier
                var maxPossibleMult = 127.0 / maxDeviation; // Max we can go without clipping
                var effectiveMult = Math.Min(intensityPercent, maxPossibleMult);
                var excess = Math.Max(0, intensityPercent - effectiveMult);

                var wroteBack = false;

                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var origColor = bmp.GetPixel(x, y);

                        // Process R (X component) and G (y) channels
                        var newR = ProcessNormalChannel(origColor.R, effectiveMult, excess);
                        var newG = ProcessNormalChannel(origColor.G, effectiveMult, excess);

                        if (newR != origColor.R || newG != origColor.G)
                        {
                            wroteBack = true;
                            var newColor = Color.FromArgb(origColor.A, newR, newG, origColor.B);
                            bmp.SetPixel(x, y, newColor);
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

        int ProcessNormalChannel(int originalValue, double effectiveMult, double excess)
        {
            // Convert to deviation from neutral (128)
            var deviation = originalValue - 128.0;

            // Apply effective multiplier
            var newDeviation = deviation * effectiveMult;

            // Apply excess at lower % effectiveness
            if (excess > 0 && Math.Abs(deviation) > 0.001) // Only if there was meaningful deviation
            {
                newDeviation += deviation * excess * NORMALMAP_EXCESS_INTENSITY_DAMPEN;
            }

            // Convert back to color value
            var newValue = 128.0 + newDeviation;

            // Standard rounding for normal map
            var finalValue = (int)Math.Round(newValue);

            return Math.Clamp(finalValue, 0, 255);
        }


        // Secondary Pass for Heightmaps
        void ProcessHeightmapsIntensity(string[] heightmapFiles)
        {
            double userIntensity = NormalIntensity / 100.0;

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

                    double currentContrast = maxGray - minGray;
                    if (currentContrast == 0)
                    {
                        // Flat image, nothing to do
                        continue;
                    }

                    // Center point for contrast
                    double center = (minGray + maxGray) / 2.0;

                    // Calculate the multiplier needed to reach full contrast (at least two pixels at 0 and 255)
                    double maxPossibleMult = 255.0 / (currentContrast);

                    // If user wants to reduce contrast
                    if (userIntensity < 1.0)
                    {
                        // Linear interpolation to center
                        for (var y = 0; y < height; y++)
                            for (var x = 0; x < width; x++)
                            {
                                var origColor = bmp.GetPixel(x, y);
                                var gray = origColor.R;
                                var newGray = center + (gray - center) * userIntensity;
                                var finalGray = (int)Math.Round(newGray);
                                finalGray = Math.Clamp(finalGray, 0, 255);
                                if (finalGray != gray)
                                    bmp.SetPixel(x, y, Color.FromArgb(origColor.A, finalGray, finalGray, finalGray));
                            }
                        WriteImageAsTGA(bmp, file);
                        continue;
                    }

                    // If userIntensity is enough to reach full contrast, but not more
                    if (userIntensity <= maxPossibleMult)
                    {
                        for (var y = 0; y < height; y++)
                            for (var x = 0; x < width; x++)
                            {
                                var origColor = bmp.GetPixel(x, y);
                                var gray = origColor.R;
                                var newGray = center + (gray - center) * userIntensity;
                                var finalGray = (int)Math.Round(newGray);
                                finalGray = Math.Clamp(finalGray, 0, 255);
                                if (finalGray != gray)
                                    bmp.SetPixel(x, y, Color.FromArgb(origColor.A, finalGray, finalGray, finalGray));
                            }
                        WriteImageAsTGA(bmp, file);
                        continue;
                    }

                    // If userIntensity exceeds maxPossibleMult, apply excess with DAMPEN
                    double excess = userIntensity - maxPossibleMult;
                    bool hasFullContrast = minGray == 0 && maxGray == 255;

                    for (var y = 0; y < height; y++)
                        for (var x = 0; x < width; x++)
                        {
                            var origColor = bmp.GetPixel(x, y);
                            var gray = origColor.R;
                            var deviation = gray - center;

                            // First, bring to full contrast
                            double newDeviation = deviation * maxPossibleMult;

                            // If image already has full contrast, or after this pass, apply excess with DAMPEN
                            if (hasFullContrast || Math.Abs(newDeviation) >= 127.5)
                            {
                                newDeviation += deviation * excess * NORMALMAP_EXCESS_INTENSITY_DAMPEN;
                            }

                            var newGray = center + newDeviation;
                            var finalGray = (int)Math.Round(newGray);
                            finalGray = Math.Clamp(finalGray, 0, 255);
                            if (finalGray != gray)
                                bmp.SetPixel(x, y, Color.FromArgb(origColor.A, finalGray, finalGray, finalGray));
                        }
                    WriteImageAsTGA(bmp, file);
                }
                catch (Exception ex)
                {
                    // MainWindow.Log($"{pack.Name}: error processing heightmap {Path.GetFileName(file)} — {ex.Message}"); ui thread no touchy
                }
            }
        }

    }



    private static void ProcessHeightmaps(PackInfo pack)
    {
        if (!pack.Enabled || string.IsNullOrEmpty(pack.Path) || !Directory.Exists(pack.Path))
            return;

        var files = Directory.GetFiles(pack.Path, "*_heightmap.tga", SearchOption.AllDirectories);
        if (!files.Any())
        {
            MainWindow.Log($"{pack.Name}: no _heightmap.tga files found.", MainWindow.LogLevel.Warning);
            return;
        }

        var alpha = ButcheredHeightmapAlpha;

        foreach (var heightmapFile in files)
        {
            try
            {
                // Find colormap file path (same file name without _heightmap suffix)
                var colormapFile = heightmapFile.Replace("_heightmap.tga", ".tga");

                if (!File.Exists(colormapFile))
                {
                    MainWindow.Log($"{pack.Name}: colormap not found for {Path.GetFileName(heightmapFile)}; skipped.", MainWindow.LogLevel.Warning);
                    continue;
                }

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
                    // MainWindow.Log($"{packName}: updated heightmap in {Path.GetFileName(heightmapFile)}.");
                }
                else
                {
                    // MainWindow.Log($"{packName}: no heightmap changes in {Path.GetFileName(heightmapFile)}.");
                }
            }
            catch (Exception ex)
            {
                MainWindow.Log($"{pack.Name}: error processing {Path.GetFileName(heightmapFile)} — {ex.Message}");
                // Updates UI which can cause freezing if too many files give error, but it is worth it as logs will appear in the end
            }
        }
    }



    private static void ProcessRoughingUp(PackInfo pack)
    {
        if (!pack.Enabled || string.IsNullOrEmpty(pack.Path) || !Directory.Exists(pack.Path))
            return;

        var files = Directory.GetFiles(pack.Path, "*_mer.tga", SearchOption.AllDirectories);
        if (!files.Any())
        {
            MainWindow.Log($"{pack.Name}: no _mer.tga files found.", MainWindow.LogLevel.Warning);
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

        foreach (var file in files)
        {
            try
            {
                using var bmp = ReadImage(file, false);
                var width = bmp.Width;
                var height = bmp.Height;

                // Check if this is an animated texture (flipbook)
                bool isAnimated = false;
                int frameHeight = width; // First frame is always square
                int frameCount = 1;

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

                for (int frame = 0; frame < frameCount; frame++)
                {
                    int frameStartY = frame * frameHeight;

                    for (var y = 0; y < frameHeight; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            int actualY = frameStartY + y;
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
    // e.g. on/off etc... but I'm not sure about it yet, or if it is event worth it.
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
                bool isVariantSuffix = false;
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
                    bool isAnimated = false;
                    int frameHeight = width; // First frame is always square
                    int frameCount = 1;

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
                        for (int y = 0; y < frameHeight; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                redOffsets[x, y] = random.Next(-materialNoiseOffset, materialNoiseOffset + 1);
                                greenOffsets[x, y] = random.Next(-materialNoiseOffset, materialNoiseOffset + 1);
                                blueOffsets[x, y] = random.Next(-materialNoiseOffset, materialNoiseOffset + 1);
                            }
                        }

                        noisePatternCache[cacheKey] = (redOffsets, greenOffsets, blueOffsets);
                    }

                    var wroteBack = false;

                    for (int frame = 0; frame < frameCount; frame++)
                    {
                        int frameStartY = frame * frameHeight;

                        for (var y = 0; y < frameHeight; y++)
                        {
                            for (var x = 0; x < width; x++)
                            {
                                int actualY = frameStartY + y;
                                var origColor = bmp.GetPixel(x, actualY);
                                int r = origColor.R;
                                int g = origColor.G;
                                int b = origColor.B;

                                // Use cached noise offsets (same for all frames and variants)
                                int redOffset = redOffsets[x, y];
                                int greenOffset = greenOffsets[x, y];
                                int blueOffset = blueOffsets[x, y];

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


