using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
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
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Vanilla_RTX_Tuner_WinUI.Core;
using Windows.Graphics;
using Windows.Storage;
using Windows.UI;
using Vanilla_RTX_Tuner_WinUI.Modules;
using static Vanilla_RTX_Tuner_WinUI.TunerVariables;
using static Vanilla_RTX_Tuner_WinUI.Core.WindowControlsManager;

namespace Vanilla_RTX_Tuner_WinUI;

/*
### GENERAL TODO & IDEAS ###

- make an invisible barrier above vessels to allow more scrolling somehow? the previewer blocks view of half the logs on min window size

- Art for the remaining 3 sliders (but what to draw??! it is impossible to convey)
- Make random startup art many, or a few, randomly set an image after initializing Previews
That way you'll have art displayed on startup as intended
- Make 3 art pieces for Vanilla RTX, Vanilla RTX Normals, and Opus, based on their cover images, for checkbox selection

- Two interesting ideas to explore further:
1. Fog intensity increase beyond 1.0: Use the excess to increase the scattering amount of Air by a certain %
e.g. someone does a 10x on a fog that is already 1.0 in density
its scattering triplets will be multipled by a toned-down number, e.g. a 10x results in a 2.5x for scattering valuesm a quarter

2. For Emissivity adjustment, Desaturate pixels towards white with the excess -- dampened
these aren't really standard adjustments, but they allow absurd values to leave an impact.


- Window goes invisible if previous save state was a monitor that is now unplugged, bound checking is messed up too

- A cool "Gradual logger" -- log texts gradually but very quickly! It helps make it less overwhelming when dumping huge logs
Besides that you're gonna need something to unify the logging
A public variable that gets all text dumped to perhaps, and gradually writes out its contents to sidebarlog whenever it is changed, async
This way direct interaction with non-UI threads will be zero
Long running tasks dump their text, UI thread gradually writes it out on its own.
only concern is performance with large logs

This idea can be a public static method and it won't ever ever block Ui thread
A variable is getting constantly updated with new logs, a worker in main UI thread's only job is to write out its content as it comes along

- Figure out a solution to keep noises the same between hardcoded pairs of blocks (e.g. redstone lamp on/off)
(Already have, an unused method, certain suffixes are matched up to share their noise pattern)

- Processor: load images once and process them, instead of doing so in multiple individual passes
You'd have to identify which types of images are going to need modifications based on packs
Then with a wee bit more complex processor class, load once and processes as needed and finally save.

This is very low prio, because files are already read nad written as TGA which is simple, super fast IO.
But it benefits Opus a lot. -- it is more managable as-is so... let the thought rest for now

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
  hook extensions, and use their manifests to figure which pack they belong to (i.e _any_, _normals_, opus_ descs)
  this way you can change which pack belongs to what upstream, and even introduce new packs without having to update tuner

  Checkboxes also good space for this, a fourth item will do.. if at least one extension is found or one addon, add-on/extensions checkboxes become selectable
  Put them in front of, in a new column to the right side of current checkboxes, perfect place for it
*/

public static class TunerVariables
{
    // When adding new variables, define the default if needed, save/load if needed, and finally account for it in UpdateUI method

    public static string? appVersion = null;

    // Pack save locations in MC folders + versions, variables are flushed and reused for Preview
    public static string VanillaRTXLocation = string.Empty;
    public static string VanillaRTXNormalsLocation = string.Empty;
    public static string VanillaRTXOpusLocation = string.Empty;

    public static string VanillaRTXVersion = string.Empty;
    public static string VanillaRTXNormalsVersion = string.Empty;
    public static string VanillaRTXOpusVersion = string.Empty;

    // For checkboxes
    public static bool IsVanillaRTXEnabled = false;
    public static bool IsNormalsEnabled = false;
    public static bool IsOpusEnabled = false;

    // Used throughout the app to target Minecraft Preview instead of regular Minecraft
    public static bool IsTargetingPreview = false;

    // Tuning variables
    public static double FogMultiplier = 1.0;
    public static double EmissivityMultiplier = 1.0;
    public static int NormalIntensity = 100;
    public static int MaterialNoiseOffset = 0;
    public static int RoughenUpIntensity = 0;
    public static int ButcheredHeightmapAlpha = 0;
    public static bool AddEmissivityAmbientLight = false;

    // Defaults backup (used as a compass for slider previews to know their defaults)
    public static class Defaults
    {
        public const double FogMultiplier = 1.0;
        public const double EmissivityMultiplier = 1.0;
        public const int NormalIntensity = 100;
        public const int MaterialNoiseOffset = 0;
        public const int RoughenUpIntensity = 0;
        public const int ButcheredHeightmapAlpha = 0;
        public const bool AddEmissivityAmbientLight = false;
    }

    // Settings we want saved and loaded upon startup, use in conjunction with UpdateUI method.
    public static void SaveSettings()
    {
        var localSettings = ApplicationData.Current.LocalSettings;

        localSettings.Values["FogMultiplier"] = FogMultiplier;
        localSettings.Values["EmissivityMultiplier"] = EmissivityMultiplier;
        localSettings.Values["NormalIntensity"] = NormalIntensity;
        localSettings.Values["MaterialNoiseOffset"] = MaterialNoiseOffset;
        localSettings.Values["RoughenUpIntensity"] = RoughenUpIntensity;
        localSettings.Values["ButcheredHeightmapAlpha"] = ButcheredHeightmapAlpha;

        localSettings.Values["AddEmissivityAmbientLight"] = AddEmissivityAmbientLight;

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

        // Force false for increase ambient lighting on each startup
        AddEmissivityAmbientLight = false; //(bool)(localSettings.Values["AddEmissivityAmbientLight"] ?? AddEmissivityAmbientLight)

        IsTargetingPreview = (bool)(localSettings.Values["TargetingPreview"] ?? IsTargetingPreview);
    }
}

// ---------------------------------------\                /-------------------------------------------- \\

public sealed partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }
    private readonly WindowStateManager _windowStateManager;
    private readonly ProgressBarManager _progressManager;
    private readonly PackUpdater _updater = new();

    private static CancellationTokenSource? _lampBlinkCts;
    private static readonly Dictionary<string, BitmapImage> _imageCache = new();

    public MainWindow()
    {
        SetMainWindowProperties();
        InitializeComponent();

        // Load settings, then update UI, image vessels are handled in UpdateUI as well
        Previewer.Initialize(PreviewVesselTop, PreviewVesselBottom, PreviewVesselBackground);
        LoadSettings();
        UpdateUI();

        _windowStateManager = new WindowStateManager(this, false, msg => Log(msg));
        _progressManager = new ProgressBarManager(ProgressBar);

        Instance = this;

        // Version and initial logs
        var version = Windows.ApplicationModel.Package.Current.Id.Version;
        var versionString = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        TitleBarText.Text = "Vanilla RTX Tuner " + versionString;
        appVersion = versionString;

        Log($"App Version: {versionString}" + new string('\n', 2) +
             "Not affiliated with Mojang Studios or NVIDIA;\nby continuing, you consent to modifications to your Minecraft data folder.");

        // Apply window state after everything is initialized
        var defaultSize = new SizeInt32(980, 720);
        _windowStateManager.ApplySavedStateOrDefaults(defaultSize);

        // Silent background credits retriever
        CreditsUpdater.GetCredits(false);

        // Warning if MC is running
        if (PackUpdater.IsMinecraftRunning() && RanOnceFlag.Set("Has_Told_User_To_Close_The_Game"))
        {
            Log("Please close Minecraft while using Tuner, when finished, launch the game using Launch Minecraft RTX button.", LogLevel.Warning);
        }

        // Set reinstall latest packs button visuals based on cache status
        if (_updater.HasDeployableCache())
        {
            UpdateVanillaRTXGlyph.Glyph = "\uE7B8";
            UpdateVanillaRTXGlyph.FontSize = 16;
        }
        else
        {
            UpdateVanillaRTXGlyph.Glyph = "\uEBD3";
            UpdateVanillaRTXGlyph.FontSize = 18;
        }

        // Release Mutex and save some of the variables upon closure
        this.Closed += (s, e) =>
        {
            SaveSettings();
            App.CleanupMutex();
        };
    }

    #region Main Window properties and essential components used throughout the app
    private void SetMainWindowProperties()
    {
        ExtendsContentIntoTitleBar = true;
        this.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;

        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = true;
            presenter.PreferredMinimumWidth = 960;
            presenter.PreferredMinimumHeight = 600;
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tuner.lamp.on.ico");
        appWindow.SetTaskbarIcon(iconPath);
        appWindow.SetTitleBarIcon(iconPath);

        // Watches theme changes and adjusts based on theme
        ThemeWatcher(this, theme =>
        {
            if (theme == ElementTheme.Light)
            {
                LightThemeBackground.Visibility = Visibility.Visible;
                TitleBarImageBrush.Opacity = 0.1;
            }
            else
            {
                LightThemeBackground.Visibility = Visibility.Collapsed;
                TitleBarImageBrush.Opacity = 0.3;
            }

            var titleBar = appWindow.TitleBar;
            if (titleBar == null) return;

            bool isLight = theme == ElementTheme.Light;

            titleBar.ButtonForegroundColor = isLight ? Colors.Black : Colors.White;
            titleBar.ButtonHoverForegroundColor = isLight ? Colors.Black : Colors.White;
            titleBar.ButtonPressedForegroundColor = isLight ? Colors.Black : Colors.White;
            titleBar.ButtonInactiveForegroundColor = isLight
                ? Color.FromArgb(255, 100, 100, 100)
                : Color.FromArgb(255, 160, 160, 160);

            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = isLight
                ? Color.FromArgb(20, 0, 0, 0)
                : Color.FromArgb(40, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = isLight
                ? Color.FromArgb(40, 0, 0, 0)
                : Color.FromArgb(60, 255, 255, 255);
        });

    }
    public static void ThemeWatcher(Window window, Action<ElementTheme> onThemeChanged)
    {
        void HookThemeChangeListener()
        {
            if (window.Content is FrameworkElement root)
            {
                root.ActualThemeChanged += (_, __) =>
                {
                    onThemeChanged(root.ActualTheme);
                };

                // also call once now
                onThemeChanged(root.ActualTheme);
            }
        }

        // Safe way to defer until content is ready
        window.Activated += (_, __) =>
        {
            HookThemeChangeListener();
        };
    }

    private void SetPreviews()
    {
        Previewer.Instance.InitializeSlider(FogMultiplierSlider,
            "ms-appx:///Assets/previews/fog.default.png",
            "ms-appx:///Assets/previews/fog.min.png",
            "ms-appx:///Assets/previews/fog.max.png",
            Defaults.FogMultiplier // default value
        );

        Previewer.Instance.InitializeSlider(EmissivityMultiplierSlider,
            "ms-appx:///Assets/previews/emissivity.default.png",
            "ms-appx:///Assets/previews/emissivity.min.png",
            "ms-appx:///Assets/previews/emissivity.max.png",
            Defaults.EmissivityMultiplier
        );

        Previewer.Instance.InitializeSlider(NormalIntensitySlider,
            "ms-appx:///Assets/previews/normals.default.png",
            "ms-appx:///Assets/previews/normals.flat.png",
            "ms-appx:///Assets/previews/normals.intense.png",
            Defaults.NormalIntensity
        );

        /*
        Previewer.Instance.InitializeSlider(MaterialNoiseSlider,
            "ms-appx:///Assets/empty.png",
            "ms-appx:///Assets/empty.png",
            "ms-appx:///Assets/empty.png",
            Defaults.MaterialNoiseOffset
        );

        Previewer.Instance.InitializeSlider(RoughenUpSlider,
            "ms-appx:///Assets/empty.png",
            "ms-appx:///Assets/empty.png",
            "ms-appx:///Assets/empty.png",
            Defaults.RoughenUpIntensity
        );

        Previewer.Instance.InitializeSlider(ButcherHeightmapsSlider,
            "ms-appx:///Assets/empty.png",
            "ms-appx:///Assets/empty.png",
            "ms-appx:///Assets/empty.png",
            Defaults.ButcheredHeightmapAlpha
        );
        */

        Previewer.Instance.InitializeToggleSwitch(EmissivityAmbientLightToggle,
            "ms-appx:///Assets/previews/emissivity.ambient.on.png",
            "ms-appx:///Assets/previews/emissivity.ambient.off.png"
        );

        Previewer.Instance.InitializeToggleButton(TargetPreviewToggle,
            "ms-appx:///Assets/previews/preview.overlay.png",
            "ms-appx:///Assets/previews/preview.png"
        );

        Previewer.Instance.InitializeButton(LocatePacksButton,
            "ms-appx:///Assets/previews/locate.png"
        );

        Previewer.Instance.InitializeButton(ExportButton,
            "ms-appx:///Assets/previews/chest.export.png"
        );

        Previewer.Instance.InitializeButton(UpdateVanillaRTXButton,
            "ms-appx:///Assets/previews/repository.reinstall.png"
        );

        Previewer.Instance.InitializeButton(TuneSelectionButton,
            "ms-appx:///Assets/previews/table.tune.png"
        );

        Previewer.Instance.InitializeButton(LaunchButton,
            "ms-appx:///Assets/previews/minecart.launch.png"
        );

        Previewer.Instance.InitializeButton(AppUpdaterButton,
            "ms-appx:///Assets/previews/repository.appupdate.png"
        );

        Previewer.Instance.InitializeButton(DonateButton,
            "ms-appx:///Assets/previews/cubeir.thankyou.png"
        );

        Previewer.Instance.InitializeButton(HelpButton,
            "ms-appx:///Assets/previews/cubeir.help.png"
        );
        /*
        Previewer.Instance.InitializeButton(ResetButton,
            "ms-appx:///Assets/empty.png"
        );
        */
    }



    public enum LogLevel
    {
        Success, Informational, Warning, Error, Network, Lengthy, Debug
    }
    public static void Log(string message, LogLevel? level = null)
    {
        void Prepend()
        {
            var textBox = Instance.SidebarLog;

            // Get the appropriate prefix for the log level (empty if no level provided)
            string prefix = level switch
            {
                LogLevel.Success => "✅ ",
                LogLevel.Informational => "ℹ️ ",
                LogLevel.Warning => "⚠️ ",
                LogLevel.Error => "❌ ",
                LogLevel.Network => "🛜 ",
                LogLevel.Lengthy => "⏳ ",
                LogLevel.Debug => "🔍 ",
                _ => ""
            };

            string prefixedMessage = $"{prefix}{message}";
            string separator = "";

            if (string.IsNullOrWhiteSpace(textBox.Text))
            {
                textBox.Text = prefixedMessage + "\n";
            }
            else
            {
                // StringBuilder better performance with large logs
                var sb = new StringBuilder(prefixedMessage.Length + textBox.Text.Length + separator.Length + 2);
                sb.Append(prefixedMessage)
                  .Append('\n')
                  .Append(separator)
                  .Append('\n')
                  .Append(textBox.Text);
                textBox.Text = sb.ToString();
            }
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
            Log("Failed to open URL. Make sure you have a browser installed and associated with web links.", LogLevel.Warning);
            Log($"Details: {ex.Message}", LogLevel.Informational);
        }
    }



    public async Task BlinkingLamp(bool enable)
    {
        const double initialDelayMs = 900; // Initial speed of blinking *also the slowest possible blinking interval*
        const double minDelayMs = 150;  // Fastest possible blinking interval 
        const double minRampSec = 1;  // Min length of how long it can on ramping up
        const double maxRampSec = 8; // Max length...
        const double fadeAnimationMs = 75; // How long does the fade animatoin take

        var onPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tuner.lamp.on.small.png");
        var superOnPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tuner.lamp.super.small.png");
        var offPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tuner.lamp.off.small.png");

        var today = DateTime.Today;
        string specialPath = null;
        bool isSpecial = false;

        if (today.Month == 4 && today.Day >= 21 && today.Day <= 23)
        {
            specialPath = Path.Combine(AppContext.BaseDirectory, "Assets", "special", "happybirthdayme.png");
            isSpecial = true;
        }
        else if (today.Month == 10 && (today.DayOfWeek == DayOfWeek.Saturday || today.DayOfWeek == DayOfWeek.Sunday))
        {
            onPath = Path.Combine(AppContext.BaseDirectory, "Assets", "special", "pumpkin.on.png");
            offPath = Path.Combine(AppContext.BaseDirectory, "Assets", "special", "pumpkin.off.png");
            superOnPath = Path.Combine(AppContext.BaseDirectory, "Assets", "special", "pumpkin.super.png");
            isSpecial = false; // Is special, but we want the halo, being special disables the halo and animation
        }
        else if (today.Month == 12 && today.Day >= 25)
        {
            specialPath = Path.Combine(AppContext.BaseDirectory, "Assets", "special", "gingerman.png");
            isSpecial = true;
        }

        // Pre-load all images (removed emptyPath)
        var imagesToPreload = new List<string> { onPath, superOnPath, offPath };
        if (specialPath != null) imagesToPreload.Add(specialPath);

        await Task.WhenAll(imagesToPreload.Select(GetCachedImageAsync));

        var random = new Random();
        _lampBlinkCts?.Cancel();
        _lampBlinkCts = null;

        if (enable)
        {
            _lampBlinkCts = new CancellationTokenSource();

            if (isSpecial)
            {
                // For special cases: show static special image, hide halo
                await SetImageAsync(iconImageBox, specialPath);
                iconOverlayImageBox.Opacity = 0; // Clear overlay instead of using empty image
                await AnimateOpacity(iconHaloImageBox, 0, fadeAnimationMs);
                return;
            }

            await BlinkLoop(_lampBlinkCts.Token, onPath, superOnPath, offPath);
        }
        else
        {
            // Static on state
            await SetImageAsync(iconImageBox, onPath);
            iconOverlayImageBox.Opacity = 0; // Clear overlay instead of using empty image
            await AnimateOpacity(iconHaloImageBox, 0.25, fadeAnimationMs);
        }

        async Task BlinkLoop(CancellationToken token, string onPath, string superOnPath, string offPath)
        {
            try
            {
                // Setup layered images
                await SetImageAsync(iconImageBox, onPath);      // Base layer (always visible)
                await SetImageAsync(iconOverlayImageBox, offPath); // Overlay layer (fades in/out)

                iconImageBox.Opacity = 1.0;
                iconOverlayImageBox.Opacity = 0.0;

                bool state = true;
                double phaseTime = 0;
                bool rampingUp = true;
                double currentRampDuration = GetRandomRampDuration();
                var rampStartTime = DateTime.UtcNow;
                var nextSuperFlash = DateTime.UtcNow.AddSeconds(random.NextDouble() * 0 + 0); // First schedule (0 seconds makes it happen the first time) 
                                                                                              // it is rescheduled again after it is triggered

                while (!token.IsCancellationRequested)
                {
                    var now = DateTime.UtcNow;

                    if (now >= nextSuperFlash)
                    {
                        // Randomly choose between two superflash varaints 10% chance to trigger
                        bool isRapidFlash = random.NextDouble() < 0.10;

                        if (isRapidFlash)
                        {
                            // Second variant: Rapid continuous flash (overcharged lamp effect)
                            var flashCount = random.Next(1, 6); // Number of rapid flashes
                            var flashSpeed = random.Next(50, 100); // random time between flashes

                            for (int i = 0; i < flashCount; i++)
                            {
                                // Flash to super bright
                                await SetImageAsync(iconImageBox, superOnPath);
                                iconOverlayImageBox.Opacity = 0;
                                var superTask = AnimateOpacity(iconHaloImageBox, 0.8, fadeAnimationMs);
                                await Task.Delay(75, token); // Hold super state for 75ms

                                // Flash back to normal on (but never off)
                                await SetImageAsync(iconImageBox, onPath);
                                var normalTask = AnimateOpacity(iconHaloImageBox, 0.5, fadeAnimationMs);
                                await Task.Delay(flashSpeed, token); // Variable speed between flashes
                            }
                        }
                        else
                        {
                            // First variant: Long-lasting static superflash
                            var superFlashDuration = random.Next(300, 1500); // 900 ms on avg

                            // Switch to super-bright mode and stay there
                            await SetImageAsync(iconImageBox, superOnPath);
                            iconOverlayImageBox.Opacity = 0;

                            // Animate to super bright state and hold the super state for the duration
                            var superBaseTask = AnimateOpacity(iconImageBox, 1.0, fadeAnimationMs);
                            var superHaloTask = AnimateOpacity(iconHaloImageBox, 0.8, fadeAnimationMs);
                            await Task.WhenAll(superBaseTask, superHaloTask);

                            await Task.Delay(superFlashDuration, token);
                        }

                        // Common cleanup for both variants - reset to normal mode
                        await SetImageAsync(iconImageBox, onPath);
                        await SetImageAsync(iconOverlayImageBox, offPath);

                        // Now animate both halo and overlay to "off" state simultaneously
                        var resetHaloTask = AnimateOpacity(iconHaloImageBox, 0.025, fadeAnimationMs);
                        var resetOverlayTask = AnimateOpacity(iconOverlayImageBox, 1.0, fadeAnimationMs);

                        // Wait for both animations to complete together
                        await Task.WhenAll(resetOverlayTask, resetHaloTask);

                        // Ensure we're in the "off" state for the next cycle
                        state = false;
                        rampingUp = true;
                        currentRampDuration = GetRandomRampDuration();
                        rampStartTime = DateTime.UtcNow;
                        nextSuperFlash = DateTime.UtcNow.AddSeconds(random.NextDouble() * 5 + 4); // Schedule next one for the next 5-9 seconds
                        continue;
                    }

                    // rEGULAR blinking with smooth transitions
                    phaseTime = (now - rampStartTime).TotalSeconds;
                    double progress = Math.Clamp(phaseTime / currentRampDuration, 0, 1);
                    double eased = EaseInOut(progress);

                    double delay = rampingUp
                        ? initialDelayMs - (initialDelayMs - minDelayMs) * eased
                        : minDelayMs + (initialDelayMs - minDelayMs) * eased;

                    // Smooth opacity transitions
                    double overlayOpacity = state ? 0.0 : 1.0; // Off image overlay
                    double normalHaloOpacity = state ? 0.5 : 0.025; // Halo intensity

                    var overlayTask = AnimateOpacity(iconOverlayImageBox, overlayOpacity, fadeAnimationMs);
                    var normalHaloTask = AnimateOpacity(iconHaloImageBox, normalHaloOpacity, fadeAnimationMs);

                    await Task.WhenAll(overlayTask, normalHaloTask);

                    state = !state;

                    if (progress >= 1.0)
                    {
                        rampingUp = !rampingUp;
                        rampStartTime = DateTime.UtcNow;
                        currentRampDuration = GetRandomRampDuration();
                    }

                    await Task.Delay((int)delay, token);
                }

                // Cleanup used to happen here, moved it to a finally to ensure it happens no matter how blinking is stopped
            }
            finally
            {
                // Final SuperFlash before cleanup - but only if we're not being cancelled by a new blinking call
                try
                {
                    // Check if cancellation was requested - if so, we might be in the middle of starting a new blink cycle
                    // We'll still do the final flash, but with a shorter duration to avoid conflicts
                    if (!token.IsCancellationRequested)
                    {
                        // Full final SuperFlash when naturally ending
                        await PerformFinalSuperFlash(onPath, superOnPath, random.Next(400, 800));
                    }
                    else
                    {
                        // Quick final SuperFlash when being cancelled (new blink cycle starting)
                        await PerformFinalSuperFlash(onPath, superOnPath, 200);
                    }
                }
                catch (OperationCanceledException)
                {
                    // If the final superflash gets cancelled, that's fine - just proceed to cleanup
                }
                catch
                {
                    // Any other exception during final flash - proceed to cleanup to ensure proper state
                }

                // Always reset to default state regardless of what happened above
                await SetImageAsync(iconImageBox, onPath);
                iconOverlayImageBox.Opacity = 0.0;
                iconImageBox.Opacity = 1.0;
                await AnimateOpacity(iconHaloImageBox, 0.25, fadeAnimationMs); // default opacity as defined in xaml
            }

            async Task PerformFinalSuperFlash(string onPath, string superOnPath, int duration)
            {
                // Switch to super-bright mode
                await SetImageAsync(iconImageBox, superOnPath);
                iconOverlayImageBox.Opacity = 0;

                // Animate to super bright state
                var superBaseTask = AnimateOpacity(iconImageBox, 1.0, fadeAnimationMs);
                var superHaloTask = AnimateOpacity(iconHaloImageBox, 0.8, fadeAnimationMs);
                await Task.WhenAll(superBaseTask, superHaloTask);

                // Hold the super state
                await Task.Delay(duration, CancellationToken.None); // Use CancellationToken.None to ensure final flash completes
            }
        }
        // -- End of blinking loop -- 


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

        async Task SetImageAsync(Image imageControl, string path)
        {
            imageControl.Source = await GetCachedImageAsync(path);
        }

        async Task AnimateOpacity(FrameworkElement element, double targetOpacity, double durationMs)
        {
            var storyboard = new Storyboard();
            var animation = new DoubleAnimation
            {
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };

            Storyboard.SetTarget(animation, element);
            Storyboard.SetTargetProperty(animation, "Opacity");
            storyboard.Children.Add(animation);

            var tcs = new TaskCompletionSource<bool>();
            storyboard.Completed += (s, e) => tcs.SetResult(true);

            storyboard.Begin();
            await tcs.Task;
        }
    }



    public async void UpdateUI(double animationDurationSeconds = 0.05)
    {
        // Hide vessels during UI updates, because previews trigger vessel updates upon value change, the conflict looks glitchy
        // This is a band-aid solution, the real solution must be implemented in previews.cs (will you?)
        PreviewVesselTop.Visibility = Visibility.Collapsed;
        PreviewVesselBottom.Visibility = Visibility.Collapsed;
        PreviewVesselBackground.Visibility = Visibility.Collapsed;

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

        // store toggle-like variables
        var boolConfigs = new[]
        {
             (EmissivityAmbientLightToggle, AddEmissivityAmbientLight)
        };


        // Handles toggle-like variables
        foreach (var (toggle, targetValue) in boolConfigs)
        {
            toggle.IsOn = targetValue;
        }

        // Handles a single slider/textbox pair
        void UpdateControl(Microsoft.UI.Xaml.Controls.Slider slider, Microsoft.UI.Xaml.Controls.TextBox textBox,
                          double startValue, double targetValue, double progress, bool isInteger = false)
        {
            var currentValue = Lerp(startValue, targetValue, progress);
            slider.Value = currentValue;
            textBox.Text = isInteger ? Math.Round(currentValue).ToString() : currentValue.ToString("0.0");
        }
        double Lerp(double start, double end, double t)
        {
            return start + (end - start) * t;
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

            await Task.Delay(4); // 16 = roughly 60 FPS
        }

        // Make sure final values are exact
        for (int i = 0; i < sliderConfigs.Length; i++)
        {
            var config = sliderConfigs[i];
            UpdateControl(config.Item1, config.Item2, config.Item3, config.Item3, 1.0, config.Item4);
        }

        // Special
        TargetPreviewToggle.IsChecked = IsTargetingPreview;


        if (RanOnceFlag.Set("Initialize_UI_Previews_Only_With_The_First_Call"))
        {
            // Initialize Previes only once, Update UI is called once at the begenning, we want previews initilized only ONCE
            // Why here? and not after UpdateUI in the MainWindow initializer?
            // Because UpdateUI runs for a few miliseconds longer, the previewer ends up setting an image based on the final value update
            // We'd want to initialize it only after the first UpdateUI method is called
            SetPreviews();
            // Log("We got art!");
            // As for other times, we manually make vessels invisible and then visible again after updating UI is done, with an empty image as defined below.
            // That way when reset button calls UpdateUI the final state will be visible and empty
            
            // Really the code below should be taking care of the the first initialization as well
            // But I just wanted to make sure I use my cool flagging class which is super useful elsewhere.

            // But no seriously, it feels slightly different, I can't put my finger on it, but initializing after updating UI in MainWindow()
            // Will end up being slightly worse
            // Here's the actual problem, upon first restart, for some god knows why reason the image vessel gets set the last thing changed by updateUI
            // EVEN THOUGH AT THE END OF UPDATE UI WE CLEARLY SET IT TO EMPTY BEFORE MAKING IT VISIBLE
            // WHAT'S WORSE: IT HAPPENS ONCE, RESTARTING THE APP AGAIN WON'T TRIGGER IT?!!??!?! WHY?!
        }

        Previewer.Instance.ClearPreviews();
        // TODO: This is the place to randomly set an splash art in the future, fade it in using setimages
        // Previewer.Instance.SetImages("ms-appx:///Assets/previews/fog.default.png", "ms-appx:///Assets/previews/fog.default.png", true);
        // Set a default image or whatever you think is a good "default" for startup/after-reset

        // Here is why this prevents the final Previewer update image from appearing
        // If you set an empty image here using Previewer.SetImage, it can be sometimes dodgy/unreliable
        // Previewer.Instance.SetImages("ms-appx:///Assets/empty.png", "ms-appx:///Assets/empty.png", true);
        // Likely because of WinUI timing issues
        // ClearPreviews specifically FADES the vessels awa using a smooth transition 
        // This smooth transition drags on longer than the final attempt of a control at updating the image vessel
        // This makes it work correctly all the time reliably, because the fading drags on after updating UI controls is finished

        // Reset opacities so fading can work again (collapsing image alone while its opacity is already at 100 from before won't do well)
        PreviewVesselTop.Opacity = 0.0;
        PreviewVesselBottom.Opacity = 0.0;
        PreviewVesselBackground.Opacity = 0.0;

        PreviewVesselTop.Visibility = Visibility.Visible;
        PreviewVesselBottom.Visibility = Visibility.Visible;
        PreviewVesselTop.Visibility = Visibility.Visible;
    }



    public void FlushTheseVariables(bool FlushLocations = false, bool FlushCheckBoxes = false, bool FlushPackVersions = false)
    {
        // TODO: Review this method, most of the time everything is flushed, so the overloads are not necessery, except where it isn't, and why?
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
        Log("Find helpful resources in the README file, launching in your browser shortly.", LogLevel.Informational);
        OpenUrl("https://github.com/Cubeir/Vanilla-RTX-Tuner/blob/master/README.md");
    }




    private void DonateButton_Click(object sender, RoutedEventArgs e)
    {
        DonateButton.Content = "\uEB52";
        var credits = CreditsUpdater.GetCredits(true);
        if (!string.IsNullOrEmpty(credits) && RanOnceFlag.Set("Wrote_Supporter_Shoutout"))
        {
            Log(credits);
        }

        OpenUrl("https://ko-fi.com/cubeir");
    }
    private void DonateButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        DonateButton.Content = "\uEB52";
        var credits = CreditsUpdater.GetCredits(true);
        if (!string.IsNullOrEmpty(credits) && RanOnceFlag.Set("Wrote_Supporter_Shoutout"))
        {
            Log(credits);
        }
    }
    private void DonateButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        DonateButton.Content = "\uEB51";
        var credits = CreditsUpdater.GetCredits(true);
        if (!string.IsNullOrEmpty(credits) && RanOnceFlag.Set("Wrote_Supporter_Shoutout"))
        {
            Log(credits);
        }
    }





    private async void AppUpdaterButton_Click(object sender, RoutedEventArgs e)
    {
        AppUpdaterButton.IsEnabled = false;

        // Downloading department: Check if we already found an update and should proceed with download/install
        // criteria is update URL and version both having been extracted from Github by AppUpdater class, otherwise we try to get them again in the following else block.
        if (!string.IsNullOrEmpty(AppUpdater.latestAppVersion) && !string.IsNullOrEmpty(AppUpdater.latestAppRemote_URL))
        {
            ToggleControls(this, false);

            _progressManager.ShowProgress();
            ToggleControls(this, false);
            _ = BlinkingLamp(true);

            var installSucess = await AppUpdater.InstallAppUpdate();
            if (installSucess.Item1)
            {
                Log("Continue in Windows App Installer.", LogLevel.Informational);
            }
            else
            {
                Log($"Automatic update failed, reason: {installSucess.Item2}\nYou can also visit the repository to download the update manually.", LogLevel.Error);
            }

            _progressManager.HideProgress();
            ToggleControls(this, true);
            _ = BlinkingLamp(false);

            // Button Visuals -> default (we're done with the update)
            AppUpdaterButton.Content = "\uE895";
            ToolTipService.SetToolTip(AppUpdaterButton, "Check for update");
            AppUpdaterButton.Background = new SolidColorBrush(Colors.Transparent);
            AppUpdaterButton.BorderBrush = new SolidColorBrush(Colors.Transparent);

            // Clear these so next time it checks for updates in the else block below
            AppUpdater.latestAppVersion = null;
            AppUpdater.latestAppRemote_URL = null;

            ToggleControls(this, true);
        }

        // Checking department: If version and URL variables aren't filled (an update isn't available) try to get them, check for updates.
        else
        {
            AppUpdaterButton.IsEnabled = false;
            _progressManager.ShowProgress();
            _ = BlinkingLamp(true);
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
                Log($"Error during update check: {ex.Message}", LogLevel.Error);
            }
            finally
            {
                AppUpdaterButton.IsEnabled = true;
                _progressManager.HideProgress();
                _ = BlinkingLamp(false);
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
        Log("Targeting Minecraft Preview.", LogLevel.Informational);
        FlushTheseVariables(true, true, true);
    }
    private void TargetPreviewToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        IsTargetingPreview = false;
        Log("Targeting regular Minecraft.", LogLevel.Informational);
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



    // Improved event handlers that don't interfere with typing
    private void FogMultiplierSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        FogMultiplier = Math.Round(e.NewValue, 2);
        if (FogMultiplierBox != null && FogMultiplierBox.FocusState == FocusState.Unfocused)
            FogMultiplierBox.Text = FogMultiplier.ToString("0.00");
    }

    private void FogMultiplierBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(FogMultiplierBox.Text, out double val))
        {
            val = Math.Clamp(val, 0.0, 7.5);
            FogMultiplier = val;
            FogMultiplierSlider.Value = val;
            FogMultiplierBox.Text = val.ToString("0.00");
        }
        else
        {
            // Reset to current value if invalid input
            FogMultiplierBox.Text = FogMultiplier.ToString("0.00");
        }
    }

    private void EmissivityMultiplierSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        EmissivityMultiplier = Math.Round(e.NewValue, 1);
        if (EmissivityMultiplierBox != null && EmissivityMultiplierBox.FocusState == FocusState.Unfocused)
            EmissivityMultiplierBox.Text = EmissivityMultiplier.ToString("F1");
    }

    private void EmissivityMultiplierBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (double.TryParse(EmissivityMultiplierBox.Text, out double val))
        {
            val = Math.Clamp(val, 0.5, 10.0);
            EmissivityMultiplier = val;
            EmissivityMultiplierSlider.Value = val;
            EmissivityMultiplierBox.Text = val.ToString("F1");
        }
        else
        {
            EmissivityMultiplierBox.Text = EmissivityMultiplier.ToString("F1");
        }
    }

    private void NormalIntensity_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        NormalIntensity = (int)Math.Round(e.NewValue);
        if (NormalIntensityBox != null && NormalIntensityBox.FocusState == FocusState.Unfocused)
            NormalIntensityBox.Text = NormalIntensity.ToString();
    }

    private void NormalIntensity_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(NormalIntensityBox.Text, out int val))
        {
            val = Math.Clamp(val, 0, 700);
            NormalIntensity = val;
            NormalIntensitySlider.Value = val;
            NormalIntensityBox.Text = val.ToString();
        }
        else
        {
            NormalIntensityBox.Text = NormalIntensity.ToString();
        }
    }

    private void MaterialNoise_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        MaterialNoiseOffset = (int)Math.Round(e.NewValue);
        if (MaterialNoiseBox != null && MaterialNoiseBox.FocusState == FocusState.Unfocused)
            MaterialNoiseBox.Text = MaterialNoiseOffset.ToString();
    }

    private void MaterialNoise_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(MaterialNoiseBox.Text, out int val))
        {
            val = Math.Clamp(val, 0, 25);
            MaterialNoiseOffset = val;
            MaterialNoiseSlider.Value = val;
            MaterialNoiseBox.Text = val.ToString();
        }
        else
        {
            MaterialNoiseBox.Text = MaterialNoiseOffset.ToString();
        }
    }

    private void RoughenUp_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        RoughenUpIntensity = (int)Math.Round(e.NewValue);
        if (RoughenUpBox != null && RoughenUpBox.FocusState == FocusState.Unfocused)
            RoughenUpBox.Text = RoughenUpIntensity.ToString();
    }

    private void RoughenUp_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(RoughenUpBox.Text, out int val))
        {
            val = Math.Clamp(val, 0, 20);
            RoughenUpIntensity = val;
            RoughenUpSlider.Value = val;
            RoughenUpBox.Text = val.ToString();
        }
        else
        {
            RoughenUpBox.Text = RoughenUpIntensity.ToString();
        }
    }

    private void ButcherHeightmaps_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        ButcheredHeightmapAlpha = (int)Math.Round(e.NewValue);
        if (ButcherHeightmapsBox != null && ButcherHeightmapsBox.FocusState == FocusState.Unfocused)
            ButcherHeightmapsBox.Text = ButcheredHeightmapAlpha.ToString();
    }

    private void ButcherHeightmaps_LostFocus(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(ButcherHeightmapsBox.Text, out int val))
        {
            val = Math.Clamp(val, 0, 255);
            ButcheredHeightmapAlpha = val;
            ButcherHeightmapsSlider.Value = val;
            ButcherHeightmapsBox.Text = val.ToString();
        }
        else
        {
            ButcherHeightmapsBox.Text = ButcheredHeightmapAlpha.ToString();
        }
    }

    // Input validation helpers - add these to prevent invalid characters
    private void IntegerTextBox_BeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
    {
        // Allow only digits
        args.Cancel = !System.Text.RegularExpressions.Regex.IsMatch(args.NewText, @"^[0-9]*$");
    }

    private void DoubleTextBox_BeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
    {
        // Allow digits, one decimal point, and leading negative sign
        args.Cancel = !System.Text.RegularExpressions.Regex.IsMatch(args.NewText, @"^-?[0-9]*\.?[0-9]*$");
    }


    private void EmissivityAmbientLightToggle_Toggled(object sender, RoutedEventArgs e)
    {
        var toggle = sender as ToggleSwitch;
        AddEmissivityAmbientLight = toggle.IsOn;

        // Show/hide the warning icon
        EmissivityWarningIcon.Visibility = toggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
    }


    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        // Defaults
        FogMultiplier = Defaults.FogMultiplier;
        EmissivityMultiplier = Defaults.EmissivityMultiplier;
        NormalIntensity = Defaults.NormalIntensity;
        MaterialNoiseOffset = Defaults.MaterialNoiseOffset;
        RoughenUpIntensity = Defaults.RoughenUpIntensity;
        ButcheredHeightmapAlpha = Defaults.ButcheredHeightmapAlpha;
        AddEmissivityAmbientLight = Defaults.AddEmissivityAmbientLight;

        // FlushTheseVariables(true, true, true);

        // Manually updates UI based on new values
        UpdateUI();

        // Empty the sidebarlog
        SidebarLog.Text = "";


        // Ignore the run-once flag for now, let the warning be said every time since we empty the log
        if (true || RanOnceFlag.Set("Said_Reset_Warning"))
        {
            RanOnceFlag.Unset("Wrote_Supporter_Shoutout");
            var text = UpdateVanillaRTXButtonText.Text;
            Log($"Note: this does not restore the packs to their default state!\nTo reset the pack back to original, use '{text as string}' button.", LogLevel.Informational);
            Log("Tuner variables reset.", LogLevel.Success);
        }
    }



    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        BlinkingLamp(true);
        _progressManager.ShowProgress();
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
                await Exporter.ExportMCPACK(path, name);

        }
        catch (Exception ex)
        {
            Log(ex.ToString(), LogLevel.Warning);
        }
        finally
        {
            if (!IsVanillaRTXEnabled && !IsNormalsEnabled && !IsOpusEnabled)
            {
                Log("Locate and select at least one package to export.", LogLevel.Warning);
            }
            else
            {
                Log("Export Queue Finished.", LogLevel.Success);
            }
            BlinkingLamp(false);
            _progressManager.HideProgress();
            ToggleControls(this, true);
        }

    }



    private async void TuneSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (PackUpdater.IsMinecraftRunning() && RanOnceFlag.Set("Has_Told_User_To_Close_The_Game"))
        {
            Log("Please close Minecraft while using Tuner, when finished, launch the game using Launch Minecraft RTX button.", LogLevel.Warning);
        }

        try
        {
            if (!IsVanillaRTXEnabled && !IsNormalsEnabled && !IsOpusEnabled)
            {
                Log("Locate and select at least one package to tune.", LogLevel.Warning);

                return;
            }
            else
            {
                _progressManager.ShowProgress();
                _ = BlinkingLamp(true);
                ToggleControls(this, false);

                await Task.Run(Processor.TuneSelectedPacks);
                Log("Completed tuning.", LogLevel.Success);

                // Turn it off
                AddEmissivityAmbientLight = false;
                EmissivityAmbientLightToggle.IsOn = false;
            }
        }
        finally
        {
            _ =BlinkingLamp(false);
            ToggleControls(this, true);
            _progressManager.HideProgress();
        }
    }



    private async void UpdateVanillaRTXButton_Click(object sender, RoutedEventArgs e)
    {
        // Set to original glyph while checking, in the end if a deployable cache is available it is set to something else again
        UpdateVanillaRTXGlyph.Glyph = "\uE8F7";
        UpdateVanillaRTXGlyph.FontSize = 18;

        if (PackUpdater.IsMinecraftRunning() && RanOnceFlag.Set("Has_Told_User_To_Close_The_Game"))
        {
            Log("Please close Minecraft while using Tuner, when finished, launch the game using Launch Minecraft RTX button.", LogLevel.Warning);
        }
        try
        {
            ToggleControls(this, false);
                _progressManager.ShowProgress();
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
                Log("Reinstallation completed.", LogLevel.Success);     
                // TODO: Trigger an artificial locate pack button click if packages were installed with success?
            }
            else
            {
                Log("Reinstallation failed.", LogLevel.Error);
            }
        }
        catch (Exception ex)
        {
            Log($"Unexpected error: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            _ = BlinkingLamp(false);
            ToggleControls(this, true);
            _progressManager.HideProgress();
            FlushTheseVariables(true, true);

            // Set reinstall latest packs button visuals based on cache status
            if (_updater.HasDeployableCache())
            {
                UpdateVanillaRTXGlyph.Glyph = "\uE7B8";
                UpdateVanillaRTXGlyph.FontSize = 16;
            }
            else
            {
                UpdateVanillaRTXGlyph.Glyph = "\uEBD3";
                UpdateVanillaRTXGlyph.FontSize = 18;
            }
        }
    }



    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        _ = BlinkingLamp(true);
        try
        {
            var logs = await Launcher.LaunchMinecraftRTXAsync(IsTargetingPreview);
            Log(logs, LogLevel.Informational);
        }
        finally
        {
            _ = BlinkingLamp(false);
            if (PackUpdater.IsMinecraftRunning())
            {
                Log("The game was already open, please restart the game for options.txt changes to take effect.", LogLevel.Warning);
            }
        }
    }
}