using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Vanilla_RTX_Tuner_WinUI.Core;
using Windows.Graphics;
using Windows.Storage;
using static Vanilla_RTX_Tuner_WinUI.TunerVariables;
using static Vanilla_RTX_Tuner_WinUI.WindowControlsManager;

namespace Vanilla_RTX_Tuner_WinUI;

/*
### TODO ###

- Change SidebarLog into a rich textbox, so it can have links and other formatting:
 - While updating the app, get link of the latest release page, and put that link in the logs "read changelogs here"
 - When user checks for updates and its available clicking one the link in sidebar log will allow them to read the changelogs.
 - It's a good feature overall, opens up options.

- Refactor and use data Binding as much as possible (as long as the change doesn't cause restrictions/complications with the control and its data)
For example, sliders must definitely be binded, make the code cleaner.

- Figure out a solution to keep noises the same between hardcoded pairs of blocks (e.g. redstone lamp on/off)

- Read UUIDs from config.json, wherever you want them -- this is necessary if you hook extensions, 
  and use their manifests to figure which pack they belong to (i.e _any_, _normals_, opus_ descs)

- Core functionality could be improved: load images once and process them, instead of doing so in multiple individual passes
  You're wasting power, slowing things down, though it is more manageable this way so perhaps.. rethink?

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
*/




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
    // TODO: Bind these (if you can do so cleanly, two-way binding seems a little worse than boilerplate-heavy)
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

// ---------------------------------------\                /--------------------------------------------//

public sealed partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }
    private WindowStateManager _windowStateManager;

    private static CancellationTokenSource _lampBlinkCts;
    private static readonly Dictionary<string, BitmapImage> _imageCache = new();

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
        Log($"App Version: {versionString}" + new string('\n', 2) +
             "This app is not affiliated with Mojang or NVIDIA;\nby continuing, you consent to modifications to your Minecraft data folder."); // shockers!

        // Silent background credits retriever
        CreditsUpdater.GetCredits(false);

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
        _windowStateManager = new WindowStateManager(this, Log);

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
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tuner.lamp.on.ico");
        appWindow.SetTaskbarIcon(iconPath);
        appWindow.SetTitleBarIcon(iconPath);
    }
    public static void Log(string message)
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
            Log("Failed to open URL. Make sure you have a browser installed and associated with web links.");
            Log($"Details: {ex.Message}");
        }
    }
    public async Task BlinkingLamp(bool enable)
    {
        const double initialDelayMs = 1000;
        const double minDelayMs = 150; // can ramp down to this Ms delay during a normal cycle
        const double minRampSec = 3; // minimum ramp cycle duration
        const double maxRampSec = 12; // maximum ...
        const double minBlinkMs = 25; // minimum erratic flash delay between blinks
        const double maxBlinkMs = 100; // maximum ...

        var onPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tuner.lamp.on.small.png");
        var superOnPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tuner.lamp.super.small.png");

        var today = DateTime.Today;
        string offPath;
        bool isSpecial;

        if (today.Month == 4 && today.Day >= 21 && today.Day <= 23)
        {
            offPath = Path.Combine(AppContext.BaseDirectory, "Assets", "special", "happybirthdayme.png");
            isSpecial = true;
        }
        else if (today.Month == 10 && (today.DayOfWeek == DayOfWeek.Saturday || today.DayOfWeek == DayOfWeek.Sunday))
        {
            onPath = Path.Combine(AppContext.BaseDirectory, "Assets", "special", "pumpkin.on.png");
            offPath = Path.Combine(AppContext.BaseDirectory, "Assets", "special", "pumpkin.off.png");
            superOnPath = Path.Combine(AppContext.BaseDirectory, "Assets", "special", "pumpkin.super.png");
            isSpecial = false; // is special, but we want to keep the blinking animation (is special is used to disable the animation)
        }
        else if (today.Month == 12 && today.Day >= 25)
        {
            offPath = Path.Combine(AppContext.BaseDirectory, "Assets", "special", "gingerman.png");
            isSpecial = true;
        }
        else
        {
            offPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tuner.lamp.off.small.png");
            isSpecial = false;
        }

        var random = new Random();
        _lampBlinkCts?.Cancel();
        _lampBlinkCts = null;

        if (enable)
        {
            _lampBlinkCts = new CancellationTokenSource();

            if (isSpecial)
            {
                await SetImageAsync(offPath);
                return;
            }

            await BlinkLoop(_lampBlinkCts.Token, onPath, superOnPath, offPath); // Pass superOnPath to BlinkLoop
        }
        else
        {
            await SetImageAsync(onPath);
        }

        async Task BlinkLoop(CancellationToken token, string onPath, string superOnPath, string offPath)
        {
            var onImage = await GetCachedImageAsync(onPath);
            var superOnImage = await GetCachedImageAsync(superOnPath); // Load super on image
            var offImage = await GetCachedImageAsync(offPath);

            bool state = true;
            double phaseTime = 0;
            bool rampingUp = true;
            double currentRampDuration = GetRandomRampDuration();
            var rampStartTime = DateTime.UtcNow;
            var nextErraticFlash = DateTime.UtcNow.AddSeconds(random.NextDouble() * 10 + 10); // schedule the first erratic flash in 10-20 sec

            while (!token.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;

                if (now >= nextErraticFlash)
                {
                    var erraticDuration = random.Next(500, 1200);
                    var erraticEnd = now.AddMilliseconds(erraticDuration);

                    while (DateTime.UtcNow < erraticEnd && !token.IsCancellationRequested)
                    {
                        iconImageBox.Source = state ? superOnImage : offImage; // Use superOnImage during erratic flash
                        state = !state;
                        await Task.Delay(random.Next((int)minBlinkMs, (int)maxBlinkMs + 1), token);
                    }

                    iconImageBox.Source = onImage; // Reset to regular on image
                    state = true;
                    rampingUp = true;
                    currentRampDuration = GetRandomRampDuration();
                    rampStartTime = DateTime.UtcNow;
                    nextErraticFlash = DateTime.UtcNow.AddSeconds(random.NextDouble() * 10 + 14); // schedule the next erratic flash (10-24 sec)
                    continue;
                }

                iconImageBox.Source = state ? onImage : offImage; // Use regular onImage during normal blinking
                state = !state;

                phaseTime = (now - rampStartTime).TotalSeconds;
                double progress = Math.Clamp(phaseTime / currentRampDuration, 0, 1);
                double eased = EaseInOut(progress);

                double delay = rampingUp
                    ? initialDelayMs - (initialDelayMs - minDelayMs) * eased
                    : minDelayMs + (initialDelayMs - minDelayMs) * eased;

                if (progress >= 1.0)
                {
                    rampingUp = !rampingUp;
                    rampStartTime = DateTime.UtcNow;
                    currentRampDuration = GetRandomRampDuration();
                }

                await Task.Delay((int)delay, token);
            }

            iconImageBox.Source = onImage; // Ensure final state is regular on image
        }

        double GetRandomRampDuration()
            => random.NextDouble() * (maxRampSec - minRampSec) + minRampSec;

        double EaseInOut(double t)
            => t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;

        async Task<BitmapImage> GetCachedImageAsync(string path)
        {
            if (_imageCache.TryGetValue(path, out var cachedImage))
            {
                return cachedImage;
            }

            using var stream = File.OpenRead(path);
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(stream.AsRandomAccessStream());
            _imageCache[path] = bmp;
            return bmp;
        }

        async Task SetImageAsync(string path)
        {
            iconImageBox.Source = await GetCachedImageAsync(path);
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
        Log("Find helpful resources in the README file, launching in your browser shortly ℹ️");

        var credits = CreditsUpdater.GetCredits(true);
        if (!string.IsNullOrEmpty(credits))
        {
            Log(credits);
        }

        OpenUrl("https://github.com/Cubeir/Vanilla-RTX-Tuner/blob/master/README.md");
    }



    private async void AppUpdaterButton_Click(object sender, RoutedEventArgs e)
    {
        // Downloading department: Check if we already found an update and should proceed with download/install
        if (!string.IsNullOrEmpty(AppUpdater.latestAppVersion) && !string.IsNullOrEmpty(AppUpdater.latestAppRemote_URL))
        {
            SidelogProgressBar.IsIndeterminate = true;
            ToggleControls(this, false);
            BlinkingLamp(true);

            var installSucess = await AppUpdater.InstallAppUpdate();
            if (installSucess.Item1)
            {
                Log("Continue in Windows App Installer.");
            }
            else
            {
                Log($"Automatic update failed, reason: {installSucess.Item2}\nYou can also visit the repository to download the update manually.");
            }

            SidelogProgressBar.IsIndeterminate = false;
            ToggleControls(this, true);
            BlinkingLamp(false);

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
            BlinkingLamp(true);
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

                    Log(updateAvailable.Item2);
                }
                else
                {
                    Log(updateAvailable.Item2);
                }
            }
            catch (Exception ex)
            {
                Log($"Error during update check: {ex.Message}");
            }
            finally
            {
                BlinkingLamp(false);
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

        Log(statusMessage);

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
        Log("Targeting Minecraft Preview.");
        FlushTheseVariables(true, true, true);
    }
    private void TargetPreviewToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        IsTargetingPreview = false;
        Log("Targeting regular Minecraft.");
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
        BlinkingLamp(true);
        SidelogProgressBar.IsIndeterminate = true;
        ToggleControls(this, false);
        try
        {
            var exportQueue = new List<(string path, string name)>();

            var suffix = $"_export_tuner_{appVersion}";
            if (IsVanillaRTXEnabled && Directory.Exists(VanillaRTXLocation))
                exportQueue.Add((VanillaRTXLocation, "Vanilla_RTX_" + VanillaRTXVersion + suffix));

            if (IsNormalsEnabled && Directory.Exists(VanillaRTXNormalsLocation))
                exportQueue.Add((VanillaRTXNormalsLocation, "Vanilla_RTX_Normals_" + VanillaRTXNormalsVersion + suffix));

            if (IsOpusEnabled && Directory.Exists(VanillaRTXOpusLocation))
                exportQueue.Add((VanillaRTXOpusLocation, "Vanilla_RTX_Opus_" + VanillaRTXOpusVersion + suffix));

            foreach (var (path, name) in exportQueue)
                await Helpers.ExportMCPACK(path, name);

        }
        catch (Exception ex)
        {
            Log(ex.ToString());
        }
        finally
        {
            if (!IsVanillaRTXEnabled && !IsNormalsEnabled && !IsOpusEnabled)
            {
                Log("Locate and select at least one package to export.");
            }
            else
            {
                Log("Export Queue Finished.");
            }
            BlinkingLamp(false);
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
                Log("Locate and select at least one package to tune.");

                return;
            }
            else
            {
                SidelogProgressBar.IsIndeterminate = true;
                BlinkingLamp(true);
                ToggleControls(this, false);

                await Task.Run(Processor.TuneSelectedPacks);
                Log("Completed tuning.");
            }
        }
        finally
        {
            BlinkingLamp(false);
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
            BlinkingLamp(true);

            var updater = new PackUpdater();

            updater.ProgressUpdate += (message) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    Log($"{message}");
                });
            };

            // Run the update operation
            var (success, logs) = await Task.Run(() => updater.UpdatePacksAsync());

            // foreach (var log in logs) Log(log);

            if (success)
            {
                Log("Reinstallation completed ✅");     
                // TODO: Trigger an artificial locate pack button click if packages were installed with success?
            }
            else
            {
                Log("Reinstallation failed❗");
            }
        }
        catch (Exception ex)
        {
            Log($"Unexpected error: {ex.Message}");
        }
        finally
        {
            BlinkingLamp(false);
            ToggleControls(this, true);
            SidelogProgressBar.IsIndeterminate = false;
            FlushTheseVariables(true, true);
        }
    }



    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        var logs = Launcher.LaunchMinecraftRTX(IsTargetingPreview);
        Log(logs);
    }
}