using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics; 
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Newtonsoft.Json;
using Windows.ApplicationModel.Calls.Background;
using Windows.Devices.Display.Core;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics;
using Windows.Graphics.Display;
using Windows.Graphics.Display.Core;
using Windows.Storage;
using WinRT.Interop;
using static Vanilla_RTX_Tuner_WinUI.TunerVariables;

namespace Vanilla_RTX_Tuner_WinUI;



public static class TunerVariables
{
    public static string downloadSaveLocation = string.Empty;

    // Pack save locations in MC folders + versions, variables are flushed and reused for Preview
    public static string VanillaRTXLocation = string.Empty;
    public static string VanillaRTXNormalsLocation = string.Empty;
    public static string VanillaRTXOpusLocation = string.Empty;

    public static string VanillaRTXVersion = string.Empty;
    public static string VanillaRTXNormalsVersion = string.Empty;
    public static string VanillaRTXOpusVersion = string.Empty;

    // Used throughout the app let functionalities know to target MC preview or not
    public static bool IsTargetingPreview = false;

    // For checkboxes, whichever is enabled is impacted by various functionalities of the app
    public static bool IsVanillaRTXEnabled = false;
    public static bool IsNormalsEnabled = false;
    public static bool IsOpusEnabled = false;

    // Tuning variables TODO: Bind these
    public static double FogMultiplier = 1.0;
    public static double EmissivityMultiplier = 1.0;
    public static int NormalIntensity = 100;
    public static int MaterialNoiseOffset = 0;
    public static int RoughenUpIntensity = 0;
    public static int ButcheredHeightmapAlpha = 0;

    public static void SaveSettings()
    {
        var localSettings = ApplicationData.Current.LocalSettings;

        localSettings.Values["FogMultiplier"] = FogMultiplier;
        localSettings.Values["EmissivityMultiplier"] = EmissivityMultiplier;
        localSettings.Values["NormalIntensity"] = NormalIntensity;
        localSettings.Values["MaterialNoiseOffset"] = MaterialNoiseOffset;
        localSettings.Values["RoughenUpIntensity"] = RoughenUpIntensity;
        localSettings.Values["ButcheredHeightmapAlpha"] = ButcheredHeightmapAlpha;
    }
    public static void LoadSettings()
    {
        var localSettings = ApplicationData.Current.LocalSettings;
        // load with fallback to initialised values
        FogMultiplier = (double)(localSettings.Values["FogMultiplier"] ?? FogMultiplier);
        EmissivityMultiplier = (double)(localSettings.Values["EmissivityMultiplier"] ?? EmissivityMultiplier);
        NormalIntensity = (int)(localSettings.Values["NormalIntensity"] ?? NormalIntensity);
        MaterialNoiseOffset = (int)(localSettings.Values["MaterialNoiseOffset"] ?? MaterialNoiseOffset);
        RoughenUpIntensity = (int)(localSettings.Values["RoughenUpIntensity"] ?? RoughenUpIntensity);
        ButcheredHeightmapAlpha = (int)(localSettings.Values["ButcheredHeightmapAlpha"] ?? ButcheredHeightmapAlpha);
    }
}

    public sealed partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }
    private WindowStateManager _windowStateManager;

    public MainWindow()
    {
        SetMainWindowProperties();
        InitializeComponent();
        LoadSettings();
        UpdateUI();

        Instance = this;

        var version = Windows.ApplicationModel.Package.Current.Id.Version; var versionString = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        TitleBarText.Text = "Vanilla RTX Tuner " + versionString;
        PushLog($"App Version: {versionString}" + new string('\n', 2) +
               "This app is not affiliated with Mojang or NVIDIA;\nby continuing, you consent to third-party modifications of your Minecraft data folder."); // shockers!

        this.Closed += (s, e) =>
        {
            SaveSettings();
            App.CleanupMutex();
        };
    }

    #region Main Window properties and essential components used throughout the app
    private void SetMainWindowProperties()
    {
        // Initialize the window state manager
        _windowStateManager = new WindowStateManager(this, PushLog);

        ExtendsContentIntoTitleBar = true;

        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        var sizeMax = new SizeInt32(980, 690);
        var sizeMin = new SizeInt32(685, 500);

        // Window Position
        _windowStateManager.ApplySavedStateOrDefaults(sizeMax);

        appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
        }
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "netherite.ico");
        appWindow.SetTaskbarIcon(iconPath);
        appWindow.SetTitleBarIcon(iconPath);
    }
    public static void PushLog(string message)
    {
        void Prepend()
        {
            string separator = "⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯";
            string wrappedMessage;

            if (string.IsNullOrWhiteSpace(Instance.SidebarLog.Text))
            {
                wrappedMessage = message + "\n";
            }
            else
            {
                wrappedMessage = message + "\n" + separator + "\n";
            }

            Instance.SidebarLog.Text = wrappedMessage + Instance.SidebarLog.Text;
        }

        if (Instance.DispatcherQueue.HasThreadAccess)
            Prepend();
        else
            Instance.DispatcherQueue.TryEnqueue(Prepend);
    }
    public static void OpenUrl(string url)
    {
        try
        {
            if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
                throw new ArgumentException("Malformed URL.");

            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            PushLog("Failed to open URL. Make sure you have a browser installed and associated with web links.");
            PushLog($"Details: {ex.Message}");
        }
    }
    public static void UpdateControlStatus(bool enabled, params Control[] controls)
    {
        foreach (var control in controls)
            control.IsEnabled = enabled;
    }
    public async void UpdateUI(double animationDurationSeconds = 0.33)
    {
        // We don't use bindings since there won't be many more sliders, but it allows us to easily get a cool animation.
        // This method should be called whenever tuning variables change.

        // store slider variable, slider and box configs, add new ones here 🍝
        var sliderConfigs = new[]
        {
            (FogMultiplierSlider, FogMultiplierBox, FogMultiplier, false),
            (EmissivityMultiplierSlider, EmissivityMultiplierBox, EmissivityMultiplier, false),
            (NormalIntensitySlider, NormalIntensityBox, (double)NormalIntensity, true),
            (MaterialNoiseSlider, MaterialNoiseBox, (double)MaterialNoiseOffset, true),
            (RoughenUpSlider, RoughenUpBox, (double)RoughenUpIntensity, true),
            (ButcherHeightmapsSlider, ButcherHeightmapsBox, (double)ButcheredHeightmapAlpha, true)
        };

        double Lerp(double start, double end, double t)
        {
            return start + (end - start) * t;
        }
        // handles a single slider/textbox pair
        void UpdateControl(Microsoft.UI.Xaml.Controls.Slider slider, Microsoft.UI.Xaml.Controls.TextBox textBox,
                          double startValue, double targetValue, double progress, bool isInteger = false)
        {
            var currentValue = Lerp(startValue, targetValue, progress);
            slider.Value = currentValue;
            textBox.Text = isInteger ? Math.Round(currentValue).ToString() : currentValue.ToString("0.0");
        }


        // Store starting values
        var startValues = sliderConfigs.Select(config => config.Item1.Value).ToArray();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var totalMs = animationDurationSeconds * 1000;

        while (stopwatch.ElapsedMilliseconds < totalMs)
        {
            var progress = stopwatch.ElapsedMilliseconds / totalMs;
            var easeProgress = 1 - Math.Pow(1 - progress, 3); // Smooth easing

            // Update all controls
            for (int i = 0; i < sliderConfigs.Length; i++)
            {
                var config = sliderConfigs[i];
                UpdateControl(config.Item1, config.Item2, startValues[i], config.Item3, easeProgress, config.Item4);
            }

            await Task.Delay(8); // 16 = roughly 60 FPS, but 120hz is the norm these days and it runs for less than a sec so its ok
        }

        // making sure final values are exact
        for (int i = 0; i < sliderConfigs.Length; i++)
        {
            var config = sliderConfigs[i];
            UpdateControl(config.Item1, config.Item2, config.Item3, config.Item3, 1.0, config.Item4);
        }
    }
    public void FlushTheseVariables(bool FlushLocations = true, bool FlushCheckBoxes = true, bool FlushPackVersions = false)
    {
        if (FlushLocations)
        {
            VanillaRTXLocation = string.Empty;
            VanillaRTXNormalsLocation = string.Empty;
            VanillaRTXOpusLocation = string.Empty;
        }
        if (FlushCheckBoxes)
        {
            VanillaRTXCheckBox.IsEnabled = false;
            VanillaRTXCheckBox.IsChecked = false;
            IsVanillaRTXEnabled = false;

            NormalsCheckBox.IsEnabled = false;
            NormalsCheckBox.IsChecked = false;
            IsNormalsEnabled = false;

            OpusCheckBox.IsEnabled = false;
            OpusCheckBox.IsChecked = false;
            IsOpusEnabled = false;

            OptionsAllCheckBox.IsEnabled = false;
            OptionsAllCheckBox.IsChecked = false;
        }
        if (FlushPackVersions)
        {
            VanillaRTXVersion = string.Empty;
            VanillaRTXNormalsVersion = string.Empty;
            VanillaRTXOpusVersion = string.Empty;
        }
        // lasang🍝 
    }
    #endregion -------------------------------
    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/Cubeir/Vanilla-RTX-Tuner/blob/master/README.md");
    }



    private async void AppUpdaterButton_Click(object sender, RoutedEventArgs e)
    {
    
    }







    private void LocatePacks_Click(object sender, RoutedEventArgs e)
    {
        FlushTheseVariables(true, true);
        try
        {
            var resolvedPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages",
                IsTargetingPreview ? "Microsoft.MinecraftWindowsBeta_8wekyb3d8bbwe" : "Microsoft.MinecraftUWP_8wekyb3d8bbwe",
                "LocalState",
                "games",
                "com.mojang",
                "resource_packs"
            );

            if (!Directory.Exists(resolvedPath))
            {
                PushLog("Resource pack directory not found, is the correct version of Minecraft installed?");
                return;
            }

            var manifestFiles = Directory.GetFiles(resolvedPath, "manifest.json", SearchOption.AllDirectories);

            var results = new List<string>();

            foreach (var file in manifestFiles)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    dynamic data = JsonConvert.DeserializeObject(json);

                    string headerUUID = data?.header?.uuid;
                    string moduleUUID = data?.modules?[0]?.uuid;
                    string folder = Path.GetDirectoryName(file)!;

                    static string FormatVersion(dynamic verArray)
                    {
                        try
                        {
                            int major = verArray[0], minor = verArray[1], patch = verArray[2];
                            return $"v{major}.{minor}.{patch}";
                        }
                        catch
                        {
                            return "";
                        }
                    }

                    string version = FormatVersion(data?.header?.version);

                    if (string.Equals(headerUUID, "a5c3cc7d-1740-4b5e-ae2c-71bc14b3f63b", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(moduleUUID, "af805084-fafa-4124-9ae2-00be4bc202dc", StringComparison.OrdinalIgnoreCase))
                    {
                        VanillaRTXLocation = folder;
                        results.Add($"Found: Vanilla RTX — {version}");
                        VanillaRTXVersion = version;
                    }
                    else if (string.Equals(headerUUID, "bbe2b225-b45b-41c2-bd3b-465cd83e6071", StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(moduleUUID, "b2eef2c6-d893-467e-b31d-cda7bf643eaa", StringComparison.OrdinalIgnoreCase))
                    {
                        VanillaRTXNormalsLocation = folder;
                        results.Add($"Found: Vanilla RTX Normals — {version}");
                        VanillaRTXNormalsVersion = version;
                    }
                    else if (string.Equals(headerUUID, "7c87f859-4d79-4d51-8887-bf450b2b2bfa", StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(moduleUUID, "be0b22f0-ad13-4bbd-81ba-b457fd9e38b8", StringComparison.OrdinalIgnoreCase))
                    {
                        VanillaRTXOpusLocation = folder;
                        results.Add($"Found: Vanilla RTX Opus — {version}");
                        VanillaRTXOpusVersion = version;
                    }

                    if (!string.IsNullOrEmpty(VanillaRTXLocation) &&
                        !string.IsNullOrEmpty(VanillaRTXNormalsLocation) &&
                        !string.IsNullOrEmpty(VanillaRTXOpusLocation))
                    {
                        break;
                    }
                        
                }
                catch
                {
                    PushLog("Malformed manifest.");
                }
            }

            if (VanillaRTXLocation == string.Empty) results.Add("Not found: Vanilla RTX");
            if (VanillaRTXNormalsLocation == string.Empty) results.Add("Not found: Vanilla RTX Normals");
            if (VanillaRTXOpusLocation == string.Empty) results.Add("Not found: Vanilla RTX Opus");

            PushLog(string.Join(Environment.NewLine, results));
        }
        catch (Exception ex)
        {
            PushLog($"Error: {ex.Message}");
        }

        // Update States of Which Packs to Modify
        if (!string.IsNullOrEmpty(VanillaRTXLocation))
        {
            VanillaRTXCheckBox.IsEnabled = true;
            VanillaRTXCheckBox.IsChecked = true;
            IsVanillaRTXEnabled = true;
        }

        if (!string.IsNullOrEmpty(VanillaRTXNormalsLocation))
        {
            NormalsCheckBox.IsEnabled = true;
            NormalsCheckBox.IsChecked = true;
            IsNormalsEnabled = true;
        }

        if (!string.IsNullOrEmpty(VanillaRTXOpusLocation))
        {
            OpusCheckBox.IsEnabled = true;
            OpusCheckBox.IsChecked = true;
            IsOpusEnabled = true;
        }


        // If any pack was found, we updated checkbox states, use checkboxes to know to enable export, tuning and selecting all checkboxes 
        if (VanillaRTXCheckBox.IsEnabled || NormalsCheckBox.IsEnabled || OpusCheckBox.IsEnabled)
        {
            OptionsAllCheckBox.IsEnabled = true;
            ExportPackages.IsEnabled = true;
            TuneSelection.IsEnabled = true;
            UpdateSelectAllState();
        }


    }




    private void TargetPreviewToggle_Checked(object sender, RoutedEventArgs e)
    {
        IsTargetingPreview = true;
        PushLog("Targeting Minecraft Preview.");
        FlushTheseVariables(true, true, true);
    }
    private void TargetPreviewToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        IsTargetingPreview = false;
        PushLog("Targeting regular Minecraft.");
        FlushTheseVariables(true, true, true);
    }




    private void SelectAll_Checked(object sender, RoutedEventArgs e)
    {
        if (VanillaRTXCheckBox.IsEnabled) VanillaRTXCheckBox.IsChecked = true;
        if (NormalsCheckBox.IsEnabled) NormalsCheckBox.IsChecked = true;
        if (OpusCheckBox.IsEnabled) OpusCheckBox.IsChecked = true;
    }
    private void SelectAll_Unchecked(object sender, RoutedEventArgs e)
    {
        if (VanillaRTXCheckBox.IsEnabled) VanillaRTXCheckBox.IsChecked = false;
        if (NormalsCheckBox.IsEnabled) NormalsCheckBox.IsChecked = false;
        if (OpusCheckBox.IsEnabled) OpusCheckBox.IsChecked = false;
    }
    private void SelectAll_Indeterminate(object sender, RoutedEventArgs e)
    {
        // Do nothing, as this state is handled by the UpdateSelectAllState method (Intellisense wrote this, boo scary!)
    }
    private void Option_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkbox)
        {
            switch (checkbox.Name)
            {
                case "VanillaRTXCheckBox":
                    IsVanillaRTXEnabled = true;
                    break;
                case "NormalsCheckBox":
                    IsNormalsEnabled = true;
                    break;
                case "OpusCheckBox":
                    IsOpusEnabled = true;
                    break;
            }
        }
        UpdateSelectAllState();
    }
    private void Option_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkbox)
        {
            switch (checkbox.Name)
            {
                case "VanillaRTXCheckBox":
                    IsVanillaRTXEnabled = false;
                    break;
                case "NormalsCheckBox":
                    IsNormalsEnabled = false;
                    break;
                case "OpusCheckBox":
                    IsOpusEnabled = false;
                    break;
            }
        }
        UpdateSelectAllState();
    }
    private void UpdateSelectAllState()
    {
        int checkedCount = new[] {
        VanillaRTXCheckBox.IsChecked == true,
        NormalsCheckBox.IsChecked == true,
        OpusCheckBox.IsChecked == true,
    }.Count(x => x);

        OptionsAllCheckBox.IsThreeState = true;

        if (checkedCount == 4)
            OptionsAllCheckBox.IsChecked = true;
        else if (checkedCount == 0)
            OptionsAllCheckBox.IsChecked = false;
        else
            OptionsAllCheckBox.IsChecked = null;
    }



    private void FogMultiplierSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        FogMultiplier = Math.Round(e.NewValue, 2);
        if (FogMultiplierBox != null)
            FogMultiplierBox.Text = FogMultiplier.ToString("0.00");
    }
    private void FogMultiplierBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (double.TryParse(FogMultiplierBox.Text, out double val))
        {
            val = Math.Clamp(val, 0.0, 10.0);
            FogMultiplier = val;
            FogMultiplierSlider.Value = val;
            FogMultiplierBox.Text = val.ToString("0.00");
        }
    }
    private void EmissivityMultiplierSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        EmissivityMultiplier = Math.Round(e.NewValue, 1);
        if (EmissivityMultiplierBox != null)
            EmissivityMultiplierBox.Text = EmissivityMultiplier.ToString("F1");
    }
    private void EmissivityMultiplierBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (double.TryParse(EmissivityMultiplierBox.Text, out double val))
        {
            val = Math.Clamp(val, 0.5, 10.0);
            EmissivityMultiplier = val;
            EmissivityMultiplierSlider.Value = val;
        }
    }
    private void NormalIntensity_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        NormalIntensity = (int)Math.Round(e.NewValue);
        if (NormalIntensityBox != null)
            NormalIntensityBox.Text = NormalIntensity.ToString();
    }
    private void NormalIntensity_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(NormalIntensityBox.Text, out int val))
        {
            val = Math.Clamp(val, 0, 600);
            NormalIntensity = val;
            NormalIntensitySlider.Value = val;
        }
    }
    private void MaterialNoise_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        MaterialNoiseOffset = (int)Math.Round(e.NewValue);
        if (MaterialNoiseBox != null)
            MaterialNoiseBox.Text = MaterialNoiseOffset.ToString();
    }
    private void MaterialNoise_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(MaterialNoiseBox.Text, out int val))
        {
            val = Math.Clamp(val, 0, 20);
            MaterialNoiseOffset = val;
            MaterialNoiseSlider.Value = val;
        }
    }
    private void RoughenUp_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        RoughenUpIntensity = (int)Math.Round(e.NewValue);
        if (RoughenUpBox != null)
            RoughenUpBox.Text = RoughenUpIntensity.ToString();
    }
    private void RoughenUp_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(RoughenUpBox.Text, out int val))
        {
            val = Math.Clamp(val, 0, 15);
            RoughenUpIntensity = val;
            RoughenUpSlider.Value = val;
        }
    }
    private void ButcherHeightmaps_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        ButcheredHeightmapAlpha = (int)Math.Round(e.NewValue);
        if (ButcherHeightmapsBox != null)
            ButcherHeightmapsBox.Text = ButcheredHeightmapAlpha.ToString();
    }
    private void ButcherHeightmaps_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(ButcherHeightmapsBox.Text, out int val))
        {
            val = Math.Clamp(val, 0, 255);
            ButcheredHeightmapAlpha = val;
            ButcherHeightmapsSlider.Value = val;
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        FogMultiplier = 1.0;
        EmissivityMultiplier = 1.0;
        NormalIntensity = 100;
        MaterialNoiseOffset = 0;
        RoughenUpIntensity = 0;
        ButcheredHeightmapAlpha = 0;

        UpdateUI();
    }




    private async void ExportPackages_Click(object sender, RoutedEventArgs e)
    {
        SidelogProgressBar.IsIndeterminate = true;
        UpdateControlStatus(false, [LocatePack, TargetPreviewToggle, UpdateCoreVanillaRTX, TuneSelection, LaunchMCRTX, ExportPackages]);
        try
        {


            var exportQueue = new List<(string path, string name)>();

            var suffix = $"_export_{DateTime.Now:yyyyMMdd_HHmmss}";
            if (IsVanillaRTXEnabled && Directory.Exists(VanillaRTXLocation))
                exportQueue.Add((VanillaRTXLocation, "Vanilla_RTX_" + VanillaRTXVersion + suffix));

            if (IsNormalsEnabled && Directory.Exists(VanillaRTXNormalsLocation))
                exportQueue.Add((VanillaRTXNormalsLocation, "Vanilla_RTX_Normals_" + VanillaRTXNormalsVersion + suffix));

            if (IsOpusEnabled && Directory.Exists(VanillaRTXOpusLocation))
                exportQueue.Add((VanillaRTXOpusLocation, "Vanilla_RTX_Opus_" + VanillaRTXOpusVersion + suffix));

            foreach (var (path, name) in exportQueue)
                await Helpers.MCPackExporter(path, name);

        }
        catch (Exception ex)
        {
            PushLog(ex.ToString());
        }
        finally
        {
            if (!IsVanillaRTXEnabled && !IsNormalsEnabled && !IsOpusEnabled)
            {
                PushLog("Locate and select at least one package to export.");
            }
            else
            {
                PushLog("Export Queue Finished.");
            }
                SidelogProgressBar.IsIndeterminate = false;
            UpdateControlStatus(true, [LocatePack, TargetPreviewToggle, UpdateCoreVanillaRTX, TuneSelection, LaunchMCRTX, ExportPackages]);
        }

    }



    private async void TuneSelection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!IsVanillaRTXEnabled && !IsNormalsEnabled && !IsOpusEnabled)
            {
                PushLog("Locate and select at least one package to tune.");

                return;
            }
            else
            {
                SidelogProgressBar.IsIndeterminate = true;
                UpdateControlStatus(false, [LocatePack, TargetPreviewToggle, UpdateCoreVanillaRTX, TuneSelection, ExportPackages, LaunchMCRTX,
                                     EmissivityMultiplierSlider, FogMultiplierSlider, NormalIntensitySlider, ButcherHeightmapsSlider, RoughenUpSlider, MaterialNoiseSlider, FogMultiplierBox, EmissivityMultiplierBox, NormalIntensityBox, ButcherHeightmapsBox, RoughenUpBox, MaterialNoiseBox,
                                     VanillaRTXCheckBox, NormalsCheckBox, OpusCheckBox, OptionsAllCheckBox]);

                await Task.Run(Core.TuneSelectedPacks);

                PushLog("Completed tuning.");

                UpdateControlStatus(true, [LocatePack, TargetPreviewToggle, UpdateCoreVanillaRTX, TuneSelection, ExportPackages, LaunchMCRTX,
                                     EmissivityMultiplierSlider, FogMultiplierSlider, NormalIntensitySlider, ButcherHeightmapsSlider, RoughenUpSlider, MaterialNoiseSlider, FogMultiplierBox, EmissivityMultiplierBox, NormalIntensityBox, ButcherHeightmapsBox, RoughenUpBox, MaterialNoiseBox, 
                                     VanillaRTXCheckBox, NormalsCheckBox, OpusCheckBox, OptionsAllCheckBox]);
                SidelogProgressBar.IsIndeterminate = false;
            }
        }
        finally
        {
            FlushTheseVariables(false, true);
        }
    }



    private async void UpdateCoreVanillaRTX_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            UpdateControlStatus(false, [LocatePack, TargetPreviewToggle, UpdateCoreVanillaRTX, TuneSelection, ExportPackages, LaunchMCRTX]);
            SidelogProgressBar.IsIndeterminate = true;



            string remoteUrl = Helpers.GetConfig<string>("remote_URL")?.Trim();
            if (string.IsNullOrWhiteSpace(remoteUrl))
            {
                PushLog("remote URL is missing or invalid.");
            }


            (bool downloadSuccess, string? downloadedSaveLocation) = await Helpers.Download(remoteUrl!);

            TunerVariables.downloadSaveLocation = downloadedSaveLocation;
            if (downloadSuccess)
            {
                await Helpers.ExtractAndDeployPacks(downloadSaveLocation);
            }
        }
        finally
        {
            UpdateControlStatus(true, [LocatePack, TargetPreviewToggle, UpdateCoreVanillaRTX, TuneSelection, ExportPackages, LaunchMCRTX]);
            SidelogProgressBar.IsIndeterminate = false;

            downloadSaveLocation = string.Empty;

            FlushTheseVariables(true, true);
        }

    }



    private void LaunchMinecraftRTX_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string optionsFilePath, protocol, versionName;

            if (IsTargetingPreview)
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
                PushLog("Failed to construct options file path.");
                return;
            }

            if (!File.Exists(optionsFilePath))
            {
                PushLog($"Options file for {versionName} not found at: {optionsFilePath}");
                PushLog("Make sure the game is installed and has been launched at least once.");
                return;
            }

            try
            {
                using (var fileStream = File.Open(optionsFilePath, FileMode.Open, FileAccess.ReadWrite))
                {
                    // file is accessible
                }
            }
            catch (UnauthorizedAccessException)
            {
                PushLog($"Access denied to options file. Please run as administrator or check file permissions.");
                return;
            }
            catch (IOException ex)
            {
                PushLog($"File is in use or inaccessible: {ex.Message}");
                return;
            }

            // read-only attribute
            try
            {
                var fileInfo = new FileInfo(optionsFilePath);
                if (fileInfo.IsReadOnly)
                {
                    fileInfo.IsReadOnly = false;
                    PushLog("Removed read-only attribute from options.txt file.");
                }
            }
            catch (Exception ex)
            {
                PushLog($"Failed to remove readonly attribute from options.txt file: {ex.Message}");
                return;
            }

            // Update graphics mode
            try
            {
                var lines = File.ReadAllLines(optionsFilePath);
                bool graphicsModeFound = false;

                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("graphics_mode:", StringComparison.OrdinalIgnoreCase))
                    {
                        var oldValue = lines[i];
                        lines[i] = "graphics_mode:3";
                        graphicsModeFound = true;
                        PushLog($"Updated graphics mode: {oldValue} -> {lines[i]}");
                        break;
                    }
                }

                if (!graphicsModeFound)
                {
                    PushLog("Warning: graphics_mode setting not found in options file. Adding it...");
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
                        PushLog($"Disabled VSync: {oldVSyncValue} -> {lines[i]}");
                        break;
                    }
                }
                if (!vsyncFound)
                {
                    PushLog("Warning: gfx_vsync setting not found. Adding it...");
                    var linesList = lines.ToList();
                    linesList.Add("gfx_vsync:0");
                    lines = linesList.ToArray();
                }

                // Create backup before writing just in case... so the app won't make people curse me
                var backupPath = optionsFilePath + ".backup";
                File.Copy(optionsFilePath, backupPath, true);
                PushLog("Created backup of options file.");

                File.WriteAllLines(optionsFilePath, lines);
                PushLog("Options file updated successfully.");
            }
            catch (Exception ex)
            {
                PushLog($"Failed to update options file: {ex.Message}");
                return;
            }

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
                PushLog($"Ray tracing enabled and {versionName} launch initiated successfully!");
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                PushLog($"Failed to launch {versionName}: {ex.Message}");
                PushLog("Make sure the game is installed and the protocol is registered.");
            }
            catch (Exception ex)
            {
                PushLog($"Unexpected error launching {versionName}: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            PushLog($"Error: {ex.Message}");
        }
    }

}