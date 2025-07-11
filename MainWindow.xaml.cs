using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Xaml.Media;
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
using Windows.Graphics;
using Windows.Storage;
using static Vanilla_RTX_Tuner_WinUI.WindowControlsManager;
using static Vanilla_RTX_Tuner_WinUI.TunerVariables;
using Vanilla_RTX_Tuner_WinUI.Core;

namespace Vanilla_RTX_Tuner_WinUI;



public static class TunerVariables
{
    public static string appVersion = null;

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

    // Tuning variables 
    // TODO: Bind these (if you can do so cleanly, two-way binding seems a little boilerplate-heavy)
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

        localSettings.Values["TargetingPreview"] = IsTargetingPreview;
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

        IsTargetingPreview = (bool)(localSettings.Values["TargetingPreview"] ?? IsTargetingPreview);
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
        appVersion = versionString;
        PushLog($"App Version: {versionString}" + new string('\n', 2) +
               "This app is not affiliated with Mojang or NVIDIA;\nby continuing, you consent to modifications to your Minecraft data folder."); // shockers!

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
    public async void UpdateUI(double animationDurationSeconds = 0.33)
    {
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


        TargetPreviewToggle.IsChecked = IsTargetingPreview;
    }
    public void FlushTheseVariables(bool FlushLocations = false, bool FlushCheckBoxes = false, bool FlushPackVersions = false)
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
        // Downloading department: Check if we already found an update and should proceed with download/install
        if (!string.IsNullOrEmpty(AppUpdater.latestAppVersion) && !string.IsNullOrEmpty(AppUpdater.latestAppRemote_URL))
        {
            AppUpdaterButton.IsEnabled = false;
            SidelogProgressBar.IsIndeterminate = true;
            ToggleControls(this, false);

            var installSucess = await AppUpdater.InstallAppUpdate();
            if (installSucess.Item1)
            {
                PushLog("Continue in Windows App Installer.");
            }
            else
            {
                PushLog($"Automatic update failed, reason: {installSucess.Item2} - Visit the repository to download the update manually.");
            }

            AppUpdaterButton.IsEnabled = true;
            SidelogProgressBar.IsIndeterminate = false;

            // Button Visuals -> default (we're done with the update)
            AppUpdaterButton.Content = "\uE895";
            ToolTipService.SetToolTip(AppUpdaterButton, "Check for updates");
            AppUpdaterButton.Background = new SolidColorBrush(Colors.Transparent);
            AppUpdaterButton.BorderBrush = new SolidColorBrush(Colors.Transparent);

            // Clear these for the next time
            AppUpdater.latestAppVersion = null;
            AppUpdater.latestAppRemote_URL = null;

        }

        // Checking department: If versoin and URL aren't both present, try to get them, check for updates.
        else
        {
            AppUpdaterButton.IsEnabled = false;
            SidelogProgressBar.IsIndeterminate = true;
            try
            {
                var updateAvailable = await AppUpdater.CheckGitHubForUpdates();

                if (updateAvailable.Item1)
                {
                    // Button Visuals -> Download Available
                    // Set icon to a "download" glyph (listed in WinUI 3.0 gallery as a part of Segoe font)
                    AppUpdaterButton.Content = "\uE896";
                    ToolTipService.SetToolTip(AppUpdaterButton, "App Update available! Click again to install");
                    var accent = (SolidColorBrush)Application.Current.Resources["SystemControlHighlightAccentBrush"];
                    AppUpdaterButton.Background = accent;
                    AppUpdaterButton.BorderBrush = accent;

                    PushLog(updateAvailable.Item2);
                }
                else
                {
                    PushLog(updateAvailable.Item2);
                }
            }
            catch (Exception ex)
            {
                PushLog($"Error during update check: {ex.Message}");
            }
            finally
            {
                AppUpdaterButton.IsEnabled = true;
                SidelogProgressBar.IsIndeterminate = false;
            }
        }
    }



    private void LocatePacksButton_Click(object sender, RoutedEventArgs e)
    {
        FlushTheseVariables(true, true, true);

        var statusMessage = PackLocator.LocatePacks(IsTargetingPreview,
            out VanillaRTXLocation, out VanillaRTXVersion,
            out VanillaRTXNormalsLocation, out VanillaRTXNormalsVersion,
            out VanillaRTXOpusLocation, out VanillaRTXOpusVersion);

        PushLog(statusMessage);

        UpdateCheckboxStates();

        // Update checkboxes depending on which packs were found
        void UpdateCheckboxStates()
        {
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

            if (VanillaRTXCheckBox.IsEnabled || NormalsCheckBox.IsEnabled || OpusCheckBox.IsEnabled)
            {
                OptionsAllCheckBox.IsEnabled = true;
                UpdateSelectAllState();
            }
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


    // TODO: Bind values and rid yourself of this mess and the UpdateUI method.
    // TODO: The textbox writing behavior is annoying, figure something.
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
            val = Math.Clamp(val, 0, 25);
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
            val = Math.Clamp(val, 0, 20);
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




    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        SidelogProgressBar.IsIndeterminate = true;
        ToggleControls(this, false);
        try
        {
            var exportQueue = new List<(string path, string name)>();

            var suffix = $"_tuner_export_{appVersion}";
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
            ToggleControls(this, true);
        }

    }



    private async void TuneSelectionButton_Click(object sender, RoutedEventArgs e)
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

                ToggleControls(this, false);

                await Task.Run(Processor.TuneSelectedPacks);
                PushLog("Completed tuning.");


            }
        }
        finally
        {
            ToggleControls(this, true);
            SidelogProgressBar.IsIndeterminate = false;
        }
    }



    private async void UpdateVanillaRTXButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ToggleControls(this, false);
            SidelogProgressBar.IsIndeterminate = true;

            var updater = new PackUpdater();

            updater.ProgressUpdate += (message) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    PushLog($"{message}");
                });
            };

            // Run the update operation
            var (success, logs) = await Task.Run(() => updater.UpdatePacksAsync());

            // foreach (var log in logs) PushLog(log);

            if (success)
            {
                PushLog("Reinstallation completed ✅");
            }
            else
            {
                PushLog("Reinstallation failed❗");
            }
        }
        catch (Exception ex)
        {
            PushLog($"Unexpected error: {ex.Message}");
        }
        finally
        {
            ToggleControls(this, true);
            SidelogProgressBar.IsIndeterminate = false;
            FlushTheseVariables(true, true);
        }
    }



    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        var logs = Launcher.LaunchMinecraftRTX(IsTargetingPreview);
        PushLog(logs);
    }

}