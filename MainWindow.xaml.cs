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
using static Vanilla_RTX_Tuner_WinUI.TunerVariables;
using static Vanilla_RTX_Tuner_WinUI.WindowControlsManager;

namespace Vanilla_RTX_Tuner_WinUI;

/*
### TODO ###

- There can be two individual images of before/after of extremes of sliders
and as you move them towards each end the image could fade between them, giving a pretty good idea of what where you're going with the slider.

^ Do this for 1.3 -- maybe focus an update on it depending on the difficulty


- Fix the funny behavior of textboxes when typing numbers

- On very high scalings and low min sizes, the scrollview doesn't kick in and controls get mashed into each other

- A cool "Gradual logger" -- log texts gradually but very quickly!
It helps make it less overwhelming when dumping huge logs
Besides that you're gonna need something to unify your logging
A public variable that gets text dumped to perhaps, and gradually writes out its contents to sidebarlog, async

- Two interesting ideas to explore further:
1. Fog intensity increase beyond 1.0: Use the excess to increase the scattering amount of Air by a certain %
e.g. someone does a 10x on a fog that is already 1.0 in density
its scattering triplets will be multipled by a toned-down number, e.g. a 10x results in a 2.5x for scattering valuesm a quarter

2. For Emissivity adjustment, Desaturate pixels towards white with the excess -- dampened
these aren't really standard adjustments, but they allow absurd values to leave an impact.

- A modern settings pane to host non-functionality related controls in the future
Such as selecting light/dark theme or auto from there (Almost every winui 3.0 app does this)
Move disclaimers, credits, etc.. in there too instead of logging them at the start
Once startup log is less busy, log KoFi member names once in a while (same 1 day CD?)

- Window goes invisible if previous save state was a monitor that is now unplugged, bound checking is messed up too

- Refactor and use data Binding as much as possible
Counter argument: if you do this, no more cool slider animations, besides, there won't be many more options and
it is managable as is, if a new slider is added, it requires additional steps of:
- It must be added in UpdateUI method
- Save/Load settings method

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
*/

public static class TunerVariables
{
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
    public static bool EmissivityAmbientLight = false;

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

        localSettings.Values["EmissivityAmbientLight"] = EmissivityAmbientLight;

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

        EmissivityAmbientLight = (bool)(localSettings.Values["EmissivityAmbientLight"] ?? EmissivityAmbientLight);

        IsTargetingPreview = (bool)(localSettings.Values["TargetingPreview"] ?? IsTargetingPreview);
    }
}

// ---------------------------------------\                /-------------------------------------------- \\

public sealed partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }
    private readonly WindowStateManager _windowStateManager;
    private readonly ProgressBarManager _progressManager;

    private static CancellationTokenSource? _lampBlinkCts;
    private static readonly Dictionary<string, BitmapImage> _imageCache = new();

    public MainWindow()
    {
        SetMainWindowProperties();
        InitializeComponent();

        // Initialize WindowStateManager    —   (Enable/disable debug logging here)
        _windowStateManager = new WindowStateManager(this, false, msg => Log(msg));
        _progressManager = new ProgressBarManager(ProgressBar);

        LoadSettings();
        UpdateUI();
        Instance = this;

        // Version and initial logs
        var version = Windows.ApplicationModel.Package.Current.Id.Version;
        var versionString = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        TitleBarText.Text = "Vanilla RTX Tuner " + versionString;
        appVersion = versionString;

        Log($"App Version: {versionString}" + new string('\n', 2) +
             "Not affiliated with Mojang Studios or NVIDIA;\nby continuing, you consent to modifications to your Minecraft data folder.");

        // Apply window state after everything is initialized
        var defaultSize = new SizeInt32(980, 690);
        _windowStateManager.ApplySavedStateOrDefaults(defaultSize);

        // Silent background credits retriever
        CreditsUpdater.GetCredits(false);

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
            presenter.PreferredMinimumWidth = 950;
            presenter.PreferredMinimumHeight = 510;
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
            string separator = "⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯⎯";

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

        var boolConfigs = new[]
        {
             (EmissivityAmbientLightToggle, EmissivityAmbientLight)
        };

        foreach (var (toggle, targetValue) in boolConfigs)
        {
            toggle.IsOn = targetValue;
        }


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
        Log("Find helpful resources in the README file, launching in your browser shortly.", LogLevel.Informational);
    }

    private void DonateButton_Click(object sender, RoutedEventArgs e)
    {
        DonateButton.Content = "\uEB52";
        var credits = CreditsUpdater.GetCredits(true);
        if (!string.IsNullOrEmpty(credits))
        {
            Log(credits);
        }

        OpenUrl("https://ko-fi.com/cubeir");
    }
    private void DonateButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        DonateButton.Content = "\uEB52";
        var credits = CreditsUpdater.GetCredits(true);
        if (!string.IsNullOrEmpty(credits))
        {
            Log(credits);
        }
    }
    private void DonateButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        DonateButton.Content = "\uEB51";
        var credits = CreditsUpdater.GetCredits(true);
        if (!string.IsNullOrEmpty(credits))
        {
            Log(credits);
        }
    }




    private async void AppUpdaterButton_Click(object sender, RoutedEventArgs e)
    {
        // Downloading department: Check if we already found an update and should proceed with download/install
        if (!string.IsNullOrEmpty(AppUpdater.latestAppVersion) && !string.IsNullOrEmpty(AppUpdater.latestAppRemote_URL))
        {
                _progressManager.ShowProgress();
            ToggleControls(this, false);
            BlinkingLamp(true);

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
            BlinkingLamp(false);

            // Button Visuals -> default (we're done with the update)
            AppUpdaterButton.Content = "\uE895";
            ToolTipService.SetToolTip(AppUpdaterButton, "Check for update");
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
            _progressManager.ShowProgress();
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
                Log($"Error during update check: {ex.Message}", LogLevel.Error);
            }
            finally
            {
                BlinkingLamp(false);
                AppUpdaterButton.IsEnabled = true;
                    _progressManager.HideProgress();
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


    private void EmissivityAmbientLightToggle_Toggled(object sender, RoutedEventArgs e)
    {
        var toggle = sender as ToggleSwitch;
        EmissivityAmbientLight = toggle.IsOn;

        // Show/hide the warning icon
        EmissivityWarningIcon.Visibility = toggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        FogMultiplier = 1.0;
        EmissivityMultiplier = 1.0;
        NormalIntensity = 100;
        MaterialNoiseOffset = 0;
        RoughenUpIntensity = 0;
        ButcheredHeightmapAlpha = 0;

        EmissivityAmbientLight = false;

        UpdateUI();
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
                await Helpers.ExportMCPACK(path, name);

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
                BlinkingLamp(true);
                ToggleControls(this, false);

                await Task.Run(Processor.TuneSelectedPacks);
                Log("Completed tuning.", LogLevel.Success);
            }
        }
        finally
        {
            BlinkingLamp(false);
            ToggleControls(this, true);
                _progressManager.HideProgress();
        }
    }



    private async void UpdateVanillaRTXButton_Click(object sender, RoutedEventArgs e)
    {
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
            BlinkingLamp(false);
            ToggleControls(this, true);
                _progressManager.HideProgress();
            FlushTheseVariables(true, true);
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
        }
    }



}