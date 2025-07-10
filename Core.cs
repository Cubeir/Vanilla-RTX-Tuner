using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using static Vanilla_RTX_Tuner_WinUI.TunerVariables;
using static Vanilla_RTX_Tuner_WinUI.Helpers;

namespace Vanilla_RTX_Tuner_WinUI;

/* Netherite
### CORE FIXES ###
- Refactor and use data Binding as much as possible (as long as the change doesn't cause restrictions/complications with the control and its data)
For example, sliders must definitely be binded, make the code cleaner.

- Make your grainy-noise method apply the SAME noise throughout stages of flipbook textures
  This is to prevent grainy PBR changes throughout animations entirely
- Also have to figure out a solution to keep noises the same between hardcoded pairs of blocks (e.g. redstone lamp on/off)
  Possibly by using the same noise seed, or taking the same values to apply -- the application will have the rules applied to it so it should be fine

- Read UUIDs from config.json, wherever you want them -- this is necessary if you hook extensions, 
  and use their manifests to figure which pack they belong to (i.e _any_, _normals_, opus_ descs)

- Core functionality could be improved: load images once and process them, instead of doing so in multiple individual passes
  You're wasting power, slowing things down, though it is more manageable this way so perhaps.. rethink?
  It could still be refactored.

### FEATURES TO ADD ###

- Reset values of sliders to original, above material noise offset, top-right empty area will be a good place for it.

- Convert for Vibrant Visuals Button (Vibrant Visualizer for short? so it fits in a button)
  Still uses mostly default vibrant visuals assets:
  wipes water-related assets, runs MERs through an automated SSS adder, scales MERs as needed to match vanilla VV style (more rough, a little grainy, and possibly more intense?)
  also wipe Fog, use vanilla vv assets wherever possible, and don't forget to properly scale emissives
  It'll be a combination of a preset of tuning + some special passes, the result should be a fully fledged tunable VV pack

- Tuner must automatically try to find Extensions and Add-Ons of each respective pack that is currently selected 
  to be tuned and queue those for tuning alongside it.
  This must be done once addons are updated and properly moved to separate pages with each pack of each variant 
  becoming standalone.
  Addons won't be explicitly selected for tuning, try to find them and tune them automatically since they're 
  natural extensions of base packs.
  One "Tune Add-Ons & Extensions" button will do, if enabled, the finding and processing of addons and extensions 
  will be automated
  Looks for all addons, the ones tagged with "any" only need this button to be modified
  the ones that have individual variants (opus/normals) need the button AND to have their packs selected for 
  modification to work

### IDEAS TO CONSIDER ###
- Make locating tell user if a new version is available by comparing against github manifests?
  If you did make sure it doesn't waste user's time if remote is unreachable. Not everyone's internets have 100% uptime, remember?

- Heightmap Intensity Maximizer -- a histogram stretch... but should you? there's already that Butcher heightmaps method.. TWO sliders that apply just to one pack? you sure?

- Preset system, the empty space near Pack Selection stackpanel is perfect, save current as preset, loading preset 
  sets the variables as they were, saved somewhere safe

- A backup and 'Restore from backup' function, let user experiment more freely, especially as more options get added 
  for tuning.
  A button that backs up all currently-installed Vanilla RTX packs and their extensions, and then restores them from backup
  Keep it simple, only one backup can exist at a time to restore from, also good to keep user from downloading the pack 
  too many times and possibly rate limiting themselves (git)
 

*/



public class Core
{

    // TODO: Don't log what's not processed, log what's processed, and refactor this mess.
    public static void TuneSelectedPacks()
    {
        MainWindow.PushLog("Tuning selected packages...");


        if (FogMultiplier != 1.0)
        {
            ProcessFog(VanillaRTXLocation, IsVanillaRTXEnabled, "Vanilla RTX");
            ProcessFog(VanillaRTXNormalsLocation, IsNormalsEnabled, "Vanilla RTX Normals");
            ProcessFog(VanillaRTXOpusLocation, IsOpusEnabled, "Vanilla RTX Opus");
        }
        else
        {
            MainWindow.PushLog("Fog multiplier is 1.0; skipped fog processing.");
        }

        if (EmissivityMultiplier != 1.0)
        {
            ProcessEmissivity(VanillaRTXLocation, IsVanillaRTXEnabled, "Vanilla RTX");
            ProcessEmissivity(VanillaRTXNormalsLocation, IsNormalsEnabled, "Vanilla RTX Normals");
            ProcessEmissivity(VanillaRTXOpusLocation, IsOpusEnabled, "Vanilla RTX Opus");
        }
        else
        {
            MainWindow.PushLog("Emissivity multiplier is 1; skipped emissivity processing.");
        }

        if (NormalIntensity != 100)
        {
            ProcessNormalIntensity(VanillaRTXNormalsLocation, IsNormalsEnabled, "Vanilla RTX Normals");
            ProcessNormalIntensity(VanillaRTXOpusLocation, IsOpusEnabled, "Vanilla RTX Opus");
        }
        else
        {
            MainWindow.PushLog("Normal Intensity at 100%; skipped Normal Map processing.");
        }

        if (MaterialNoiseOffset != 0)
        {
            ProcessMaterialNoise(VanillaRTXLocation, IsVanillaRTXEnabled, "Vanilla RTX");
            ProcessMaterialNoise(VanillaRTXNormalsLocation, IsNormalsEnabled, "Vanilla RTX Normals");
            ProcessMaterialNoise(VanillaRTXOpusLocation, IsOpusEnabled, "Vanilla RTX Opus");
        }
        else
        {
            MainWindow.PushLog("Material Noise Maximum Offset at 0; skipped noise processing");
        }

        if (RoughenUpIntensity != 0)
        {
            ProcessRoughingUp(VanillaRTXLocation, IsVanillaRTXEnabled, "Vanilla RTX");
            ProcessRoughingUp(VanillaRTXNormalsLocation, IsNormalsEnabled, "Vanilla RTX Normals");
            ProcessRoughingUp(VanillaRTXOpusLocation, IsOpusEnabled, "Vanilla RTX Opus");
        }
        else
        {
            MainWindow.PushLog("Roughen Up intensity at 0; skipped roughness processing");
        }


        if (ButcheredHeightmapAlpha != 0)
        {
            ProcessHeightmaps(VanillaRTXLocation, IsVanillaRTXEnabled, "Vanilla RTX");
        }
        else
        {
            MainWindow.PushLog("Butcherd Heightmap Alpha at 0; skipped heightmap processing");
        }



    }
    #region ------------------- Processors
    private static void ProcessFog(string packPath, bool isEnabled, string packName)
    {
        bool uniformDensity = GetConfig<bool>("remove_height_based_fog");

        if (!isEnabled || string.IsNullOrEmpty(packPath) || !Directory.Exists(packPath))
            return;

        string[] files = Directory.GetFiles(packPath, "*_volumetric_fog_setting.json", SearchOption.AllDirectories);
        if (!files.Any())
        {
            // MainWindow.PushLog($"{packName}: no fog setting files found.");
            return;
        }

        foreach (string file in files)
        {
            try
            {
                string text = File.ReadAllText(file);
                JObject root = JObject.Parse(text);

                // volumetric density
                var fogSettings = root.SelectToken("minecraft:fog_settings") as JObject;
                var volumetric = fogSettings?.SelectToken("volumetric") as JObject;
                var density = volumetric?.SelectToken("density") as JObject;

                if (density == null)
                {
                    // MainWindow.PushLog($"{packName}: invalid structure in {Path.GetFileName(file)} - no density section found.");
                    continue;
                }

                bool modified = false;

                modified |= ProcessDensitySection(density, "air", uniformDensity);
                modified |= ProcessDensitySection(density, "weather", uniformDensity);

                if (modified)
                {
                    File.WriteAllText(file, root.ToString(Newtonsoft.Json.Formatting.Indented));
                    // MainWindow.PushLog($"{packName}: updated fog densities in {Path.GetFileName(file)}.");
                }
                else
                {
                    // MainWindow.PushLog($"{packName}: no fog density values to update in {Path.GetFileName(file)}.");
                }
            }
            catch (Exception ex)
            {
                // MainWindow.PushLog($"{packName}: error processing {Path.GetFileName(file)} — {ex.Message}");
            }
        }

        bool ProcessDensitySection(JObject densityParent, string sectionName, bool makeUniform)
        {
            var section = densityParent.SelectToken(sectionName) as JObject;
            if (section == null)
                return false;

            bool sectionModified = false;
            var maxDensityToken = section.SelectToken("max_density");

            if (maxDensityToken != null)
            {
                if (TryGetDensityValue(maxDensityToken, out double currentDensity))
                {
                    double newDensity = CalculateNewDensity(currentDensity, FogMultiplier);

                    // Only update files if the value meaningfully changed
                    if (Math.Abs(newDensity - currentDensity) >= 0.000001)
                    {
                        section["max_density"] = newDensity;
                        sectionModified = true;
                    }
                }
            }

            if (makeUniform)
            {
                var uniformToken = section.SelectToken("uniform");
                var maxDensityHeightToken = section.SelectToken("max_density_height");
                var zeroDensityHeightToken = section.SelectToken("zero_density_height");

                bool isHeightBased = (maxDensityHeightToken != null || zeroDensityHeightToken != null) &&
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



    private static void ProcessEmissivity(string packPath, bool isEnabled, string packName)
    {
        double emissiveExcessDampen = GetConfig<double>("excess_emissive_intensity_dampening_constant");
        if (!isEnabled || string.IsNullOrEmpty(packPath) || !Directory.Exists(packPath))
            return;
        var files = Directory.GetFiles(packPath, "*_mer.tga", SearchOption.AllDirectories);
        if (!files.Any())
        {
            MainWindow.PushLog($"{packName}: no _mer.tgas files found.");
            return;
        }
        var userMult = EmissivityMultiplier;
        foreach (var file in files)
        {
            try
            {
                using Bitmap bmp = ReadImage(file, false);
                int width = bmp.Width;
                int height = bmp.Height;
                // Max green value within image
                int maxGreen = 0;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int g = bmp.GetPixel(x, y).G;
                        if (g > maxGreen) maxGreen = g;
                    }
                }
                // No green pixels? skip processing
                if (maxGreen == 0)
                {
                    // MainWindow.PushLog($"{packName}: {Path.GetFileName(file)} has no emissive pixels; skipped.");
                    continue;
                }
                
                // Excess Multiplier Dampner
                double ratio = 255.0 / maxGreen;
                double neededMult = ratio;
                double effectiveMult = userMult < neededMult ? userMult : neededMult;
                double excess = Math.Max(0, userMult - effectiveMult);

                // Process
                bool wroteBack = false;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Color origColor = bmp.GetPixel(x, y);
                        int origG = origColor.G;
                        if (origG == 0)
                            continue;
                        // Multiply all by "effective" or necessary portion of the multiplier
                        double newG = origG * effectiveMult;
                        // Apply excess of multiplier to the rest at % effectiveness to partially preserve color composition
                        if (excess > 0)
                        {
                            newG += origG * excess * emissiveExcessDampen;
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
                            Color newColor = Color.FromArgb(origColor.A, origColor.R, finalG, origColor.B);
                            bmp.SetPixel(x, y, newColor);
                        }
                    }
                }
                if (wroteBack)
                {
                    WriteImageAsTGA(bmp, file);
                    // MainWindow.PushLog($"{packName}: updated emissivity in {Path.GetFileName(file)}.");
                }
                else
                {
                    // MainWindow.PushLog($"{packName}: no emissivity changes in {Path.GetFileName(file)}.");
                }
            }
            catch (Exception ex)
            {
                  MainWindow.PushLog($"{packName}: error processing {Path.GetFileName(file)} — {ex.Message}");
                 // Updates UI which can cause freezing if too many files give error, but it is worth it as logs will appear in the end
            }
        }
    }



    private static void ProcessNormalIntensity(string packPath, bool isEnabled, string packName)
    {
        double normalExcessDampen = GetConfig<double>("excess_normal_intensity_dampening_constant");
        if (!isEnabled || string.IsNullOrEmpty(packPath) || !Directory.Exists(packPath))
            return;

        var allNormalFiles = Directory.GetFiles(packPath, "*_normal.tga", SearchOption.AllDirectories);
        var files = new List<string>();

        foreach (var file in allNormalFiles)
        {
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(file);

            // Check if a double-normal variant exists (some blocks already end with _normal suffix)
            string doubleNormalPath = Path.Combine(Path.GetDirectoryName(file), fileNameWithoutExt + "_normal.tga");

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
            MainWindow.PushLog($"{packName}: no _normal.tga files found.");
            return;
        }

        double intensityPercent = NormalIntensity / 100.0; // percentage -> multiplier

        foreach (var file in files)
        {
            try
            {
                using Bitmap bmp = ReadImage(file, false);
                int width = bmp.Width;
                int height = bmp.Height;

                // Find maximum deviation from 128 in R and G channels
                double maxDeviation = 0;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Color pixel = bmp.GetPixel(x, y);
                        double rDev = Math.Abs(pixel.R - 128.0);
                        double gDev = Math.Abs(pixel.G - 128.0);
                        double totalDev = Math.Max(rDev, gDev);
                        if (totalDev > maxDeviation)
                            maxDeviation = totalDev;
                    }
                }
                if (maxDeviation == 0)
                {
                    // MainWindow.PushLog($"{packName}: {Path.GetFileName(file)} has no normal data; skipped.");
                    continue;
                }

                // Calculate effective multiplier
                double maxPossibleMult = 127.0 / maxDeviation; // Max we can go without clipping
                double effectiveMult = Math.Min(intensityPercent, maxPossibleMult);
                double excess = Math.Max(0, intensityPercent - effectiveMult);

                bool wroteBack = false;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Color origColor = bmp.GetPixel(x, y);

                        // Process R (X component) and G (y) channels
                        int newR = ProcessNormalChannel(origColor.R, effectiveMult, excess);
                        int newG = ProcessNormalChannel(origColor.G, effectiveMult, excess);

                        if (newR != origColor.R || newG != origColor.G)
                        {
                            wroteBack = true;
                            Color newColor = Color.FromArgb(origColor.A, newR, newG, origColor.B);
                            bmp.SetPixel(x, y, newColor);
                        }
                    }
                }

                if (wroteBack)
                {
                    WriteImageAsTGA(bmp, file);
                    // MainWindow.PushLog($"{packName}: updated normal intensity in {Path.GetFileName(file)}.");
                }
                else
                {
                    // MainWindow.PushLog($"{packName}: no normal intensity changes in {Path.GetFileName(file)}.");
                }
            }
            catch (Exception ex)
            {
                // MainWindow.PushLog($"{packName}: error processing {Path.GetFileName(file)} — {ex.Message}");
            }
        }

        int ProcessNormalChannel(int originalValue, double effectiveMult, double excess)
        {
            // Convert to deviation from neutral (128)
            double deviation = originalValue - 128.0;

            // Apply effective multiplier
            double newDeviation = deviation * effectiveMult;

            // Apply excess at lower % effectiveness
            if (excess > 0 && Math.Abs(deviation) > 0.001) // Only if there was meaningful deviation
            {
                newDeviation += deviation * excess * normalExcessDampen;
            }

            // Convert back to color value
            double newValue = 128.0 + newDeviation;

            // Standard rounding for normal map
            int finalValue = (int)Math.Round(newValue);

            return Math.Clamp(finalValue, 0, 255);
        }
    }



    private static void ProcessHeightmaps(string packPath, bool isEnabled, string packName)
    {
        if (!isEnabled || string.IsNullOrEmpty(packPath) || !Directory.Exists(packPath))
            return;

        var files = Directory.GetFiles(packPath, "*_heightmap.tga", SearchOption.AllDirectories);
        if (!files.Any())
        {
            MainWindow.PushLog($"{packName}: no _heightmap.tga files found.");
            return;
        }

        var alpha = ButcheredHeightmapAlpha;

        foreach (var heightmapFile in files)
        {
            try
            {
                // Find colormap file path (same file name without _heightmap suffix)
                string colormapFile = heightmapFile.Replace("_heightmap.tga", ".tga");

                if (!File.Exists(colormapFile))
                {
                    MainWindow.PushLog($"{packName}: colormap not found for {Path.GetFileName(heightmapFile)}; skipped.");
                    continue;
                }

                using Bitmap heightmapBmp = ReadImage(heightmapFile, false);
                using Bitmap colormapBmp = ReadImage(colormapFile, false);

                int width = heightmapBmp.Width;
                int height = heightmapBmp.Height;

                // Ensure dimensions match
                if (colormapBmp.Width != width || colormapBmp.Height != height)
                {
                    MainWindow.PushLog($"{packName}: dimension mismatch between heightmap and colormap for {Path.GetFileName(heightmapFile)}; skipped.");
                    continue;
                }

                // 1: Convert colormap to greyscale and find min/max values
                byte[,] greyscaleValues = new byte[width, height];
                byte minValue = 255;
                byte maxValue = 0;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Color color = colormapBmp.GetPixel(x, y);
                        // Standard greyscale conversion
                        byte greyValue = (byte)(0.299 * color.R + 0.587 * color.G + 0.114 * color.B);
                        greyscaleValues[x, y] = greyValue;

                        if (greyValue < minValue) minValue = greyValue;
                        if (greyValue > maxValue) maxValue = greyValue;
                    }
                }

                // 2: Stretch/Maximize
                byte[,] stretchedValues = new byte[width, height];
                double range = maxValue - minValue;

                if (range == 0)
                {
                    // All pixels are the same value
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            stretchedValues[x, y] = 128;
                        }
                    }
                }
                else
                {
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            double normalized = (greyscaleValues[x, y] - minValue) / range;
                            stretchedValues[x, y] = (byte)(normalized * 255);
                        }
                    }
                }

                // Step 3: Overlay stretched heightmap on original heightmap
                bool wroteBack = false;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Color origColor = heightmapBmp.GetPixel(x, y);
                        byte newHeightValue = stretchedValues[x, y];

                        // Alpha blend
                        int blendedValue = (alpha * newHeightValue + (255 - alpha) * origColor.R) / 255;
                        byte finalValue = (byte)Math.Clamp(blendedValue, 0, 255);

                        if (finalValue != origColor.R)
                        {
                            wroteBack = true;
                            Color newColor = Color.FromArgb(origColor.A, finalValue, finalValue, finalValue);
                            heightmapBmp.SetPixel(x, y, newColor);
                        }
                    }
                }

                if (wroteBack)
                {
                    WriteImageAsTGA(heightmapBmp, heightmapFile);
                    // MainWindow.PushLog($"{packName}: updated heightmap in {Path.GetFileName(heightmapFile)}.");
                }
                else
                {
                    // MainWindow.PushLog($"{packName}: no heightmap changes in {Path.GetFileName(heightmapFile)}.");
                }
            }
            catch (Exception ex)
            {
                MainWindow.PushLog($"{packName}: error processing {Path.GetFileName(heightmapFile)} — {ex.Message}");
                // Updates UI which can cause freezing if too many files give error, but it is worth it as logs will appear in the end
            }
        }
    }



    private static void ProcessRoughingUp(string packPath, bool isEnabled, string packName)
    {
        if (!isEnabled || string.IsNullOrEmpty(packPath) || !Directory.Exists(packPath))
            return;

        var files = Directory.GetFiles(packPath, "*_mer.tga", SearchOption.AllDirectories);
        if (!files.Any())
        {
            MainWindow.PushLog($"{packName}: no _mer.tga files found.");
            return;
        }

        var amount = RoughenUpIntensity;
        if (amount <= 0)
            return;

        foreach (var file in files)
        {
            try
            {
                using Bitmap bmp = ReadImage(file, false);
                int width = bmp.Width;
                int height = bmp.Height;

                bool wroteBack = false;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Color origColor = bmp.GetPixel(x, y);
                        int origB = origColor.B;

                        // a curve where low values get high boost, high values get minimal boost
                        double normalized = origB / 255.0; // 0 to 1
                        double power = 3.0; // Higher power = a more aggressive curve
                        double inverseFactor = 1.0 - Math.Pow(normalized, power);

                        // At intensity 10: value 1 gets ~+20, value 40 gets ~+18, value 128 gets ~+5, value 190 gets ~+1
                        double maxBoost = 20.0;
                        double boost = (amount / 10.0) * maxBoost * inverseFactor;

                        double newB = origB + boost;
                        int finalB = (int)Math.Round(newB);
                        finalB = Math.Clamp(finalB, 0, 255);

                        if (finalB != origB)
                        {
                            wroteBack = true;
                            Color newColor = Color.FromArgb(origColor.A, origColor.R, origColor.G, finalB);
                            bmp.SetPixel(x, y, newColor);
                        }
                    }
                }

                if (wroteBack)
                {
                    WriteImageAsTGA(bmp, file);
                    // MainWindow.PushLog($"{packName}: updated roughness in {Path.GetFileName(file)}.");
                }
                else
                {
                    // MainWindow.PushLog($"{packName}: no roughness changes in {Path.GetFileName(file)}.");
                }
            }
            catch (Exception ex)
            {
                MainWindow.PushLog($"{packName}: error processing {Path.GetFileName(file)} — {ex.Message}");
                // Updates UI which can cause freezing if too many files give error, but it is worth it as logs will appear in the end
            }
        }
    }



    private static void ProcessMaterialNoise(string packPath, bool isEnabled, string packName)
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
                // Linear fall-off from 128 to 255: 100% at 128, 67% at 255
                return 1.0 - ((colorValue - 128) * 0.33 / 127.0);
            }
        }

        if (!isEnabled || string.IsNullOrEmpty(packPath) || !Directory.Exists(packPath))
            return;

        var files = Directory.GetFiles(packPath, "*_mer.tga", SearchOption.AllDirectories);
        if (!files.Any())
        {
            MainWindow.PushLog($"{packName}: no _mer.tga files found.");
            return;
        }

        int materialNoiseOffset = MaterialNoiseOffset;
        if (materialNoiseOffset <= 0)
            return;

        Random random = new Random();

        foreach (var file in files)
        {
            try
            {
                using Bitmap bmp = ReadImage(file, false);
                int width = bmp.Width;
                int height = bmp.Height;

                bool wroteBack = false;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Color origColor = bmp.GetPixel(x, y);
                        int r = origColor.R;
                        int g = origColor.G;
                        int b = origColor.B;

                        // unique random offsets
                        int redOffset = random.Next(-materialNoiseOffset, materialNoiseOffset + 1);
                        int greenOffset = random.Next(-materialNoiseOffset, materialNoiseOffset + 1);
                        int blueOffset = random.Next(-materialNoiseOffset, materialNoiseOffset + 1);

                        // effectiveness based on current color values
                        double redEffectiveness = CalculateEffectiveness(r);
                        double greenEffectiveness = CalculateEffectiveness(g) * 0.25; // keep green at 1/4 effectiveness, no one likes grainy emissives
                        double blueEffectiveness = CalculateEffectiveness(b);

                        // set effectiveness of offsets, rounded
                        int effectiveRedOffset = (int)Math.Round(redOffset * redEffectiveness);
                        int effectiveGreenOffset = (int)Math.Round(greenOffset * greenEffectiveness);
                        int effectiveBlueOffset = (int)Math.Round(blueOffset * blueEffectiveness);

                        
                        int newR = r + effectiveRedOffset;
                        int newG = g + effectiveGreenOffset;
                        int newB = b + effectiveBlueOffset;

                        // anti-clipping rule: discard if would cause clipping, keep original colors!
                        if (newR < 0 || newR > 255) newR = r;
                        if (newG < 0 || newG > 255) newG = g;
                        if (newB < 0 || newB > 255) newB = b;

                        if (newR != r || newG != g || newB != b)
                        {
                            wroteBack = true;
                            Color newColor = Color.FromArgb(origColor.A, newR, newG, newB);
                            bmp.SetPixel(x, y, newColor);
                        }
                    }
                }

                if (wroteBack)
                {
                    WriteImageAsTGA(bmp, file);
                    // MainWindow.PushLog($"{packName}: added material noise to {Path.GetFileName(file)}.");
                }
                else
                {
                    // MainWindow.PushLog($"{packName}: no changes in material noise for {Path.GetFileName(file)}.");
                }
            }
            catch (Exception ex)
            {
                MainWindow.PushLog($"{packName}: error processing {Path.GetFileName(file)} — {ex.Message}");
                // Updates UI which can cause freezing if too many files give error, but it is worth it as logs will appear in the end
            }
        }
    }




    #endregion Processors -------------------
}


