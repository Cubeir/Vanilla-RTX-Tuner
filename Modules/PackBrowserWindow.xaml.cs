using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Vanilla_RTX_Tuner_WinUI.Core;
using Windows.Storage;
using WinRT.Interop;

namespace Vanilla_RTX_Tuner_WinUI.PackBrowser;

public sealed partial class PackBrowserWindow : Window
{
    private readonly AppWindow _appWindow;

    public PackBrowserWindow()
    {
        this.InitializeComponent();

        // Remove title bar and hide system buttons
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow.TitleBar != null)
        {
            _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            _appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            _appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            // Hide all system buttons
            _appWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
        }

        // Set title bar drag region
        this.Activated += PackBrowserWindow_Activated;
    }

    private async void PackBrowserWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            this.Activated -= PackBrowserWindow_Activated;

            // Delay drag region setup until UI is fully loaded
            _ = this.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                SetTitleBarDragRegion();
            });

            await LoadPacksAsync();
        }
    }

    private void SetTitleBarDragRegion()
    {
        if (_appWindow.TitleBar != null && TitleBarArea.XamlRoot != null)
        {
            try
            {
                var scaleAdjustment = TitleBarArea.XamlRoot.RasterizationScale;
                var dragRectWidth = (int)((TitleBarArea.ActualWidth - CloseButton.ActualWidth) * scaleAdjustment);
                var dragRectHeight = (int)(TitleBarArea.ActualHeight * scaleAdjustment);

                if (dragRectWidth > 0 && dragRectHeight > 0)
                {
                    var dragRect = new Windows.Graphics.RectInt32(0, 0, dragRectWidth, dragRectHeight);
                    _appWindow.TitleBar.SetDragRectangles(new[] { dragRect });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting drag region: {ex.Message}");
            }
        }
    }

    private async Task LoadPacksAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("Starting pack scan...");

            var packs = await ScanForCompatiblePacksAsync();

            System.Diagnostics.Debug.WriteLine($"Found {packs.Count} packs");

            LoadingPanel.Visibility = Visibility.Collapsed;
            PackSelectionPanel.Visibility = Visibility.Visible;

            if (packs.Count == 0)
            {
                EmptyStatePanel.Visibility = Visibility.Visible;
                EmptyStateText.Text = TunerVariables.IsTargetingPreview
                    ? "No compatible packs found in Minecraft Preview data directory."
                    : "No compatible packs found in Minecraft data directory.";
                return;
            }

            foreach (var pack in packs)
            {
                System.Diagnostics.Debug.WriteLine($"Creating button for: {pack.PackName}");
                var packButton = CreatePackButton(pack);
                PackListContainer.Children.Add(packButton);
            }

            System.Diagnostics.Debug.WriteLine("Pack loading complete");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"EXCEPTION in LoadPacksAsync: {ex}");

            LoadingPanel.Visibility = Visibility.Collapsed;
            PackSelectionPanel.Visibility = Visibility.Visible;
            EmptyStatePanel.Visibility = Visibility.Visible;
            EmptyStateText.Text = $"Error: {ex.Message}";
        }
    }

    private Button CreatePackButton(PackData pack)
    {
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12),
            Tag = pack
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Icon
        var iconBorder = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(6),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Gray)
        };

        if (pack.Icon != null)
        {
            iconBorder.Child = new Image
            {
                Source = pack.Icon,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
            };
        }
        else
        {
            iconBorder.Child = new FontIcon
            {
                Glyph = "\uE8B7",
                FontSize = 32,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        Grid.SetColumn(iconBorder, 0);
        grid.Children.Add(iconBorder);

        // Pack info (left side)
        var infoPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center
        };

        var nameText = new TextBlock
        {
            Text = pack.PackName,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var descriptionText = new TextBlock
        {
            Text = pack.PackDescription,
            FontSize = 12,
            Opacity = 0.75,
            Margin = new Thickness(0, 2, 0, 0),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        infoPanel.Children.Add(nameText);
        infoPanel.Children.Add(descriptionText);

        Grid.SetColumn(infoPanel, 2);
        grid.Children.Add(infoPanel);

        // Capability Tags (right side, bottom aligned)
        var tagsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
            Spacing = 6
        };

        foreach (var tag in pack.CapabilityTags)
        {
            var tagBorder = new Border
            {
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(175, 105, 105, 105)), // Dim grey
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4, 8, 4)
            };

            var tagText = new TextBlock
            {
                Text = tag,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(175, 244, 244, 244)) // Light grey text
            };

            tagBorder.Child = tagText;
            tagsPanel.Children.Add(tagBorder);
        }

        Grid.SetColumn(tagsPanel, 4);
        grid.Children.Add(tagsPanel);

        button.Content = grid;
        button.Click += PackButton_Click;

        return button;
    }

    private void PackButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is PackData packData)
        {
            TunerVariables.CustomPackLocation = packData.PackPath;
            TunerVariables.CustomPackDisplayName = packData.PackName;
            this.Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private async Task<List<PackData>> ScanForCompatiblePacksAsync()
    {
        var packs = new List<PackData>();

        string basePath;
        if (TunerVariables.IsTargetingPreview)
        {
            basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Minecraft Bedrock Preview"
            );
        }
        else
        {
            basePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Minecraft Bedrock"
            );
        }

        var scanPaths = new[]
        {
        Path.Combine(basePath, "Users", "Shared", "games", "com.mojang", "resource_packs"),
        Path.Combine(basePath, "Users", "Shared", "games", "com.mojang", "development_resource_packs")
    };

        foreach (var scanPath in scanPaths)
        {
            if (!Directory.Exists(scanPath))
            {
                System.Diagnostics.Debug.WriteLine($"Path doesn't exist: {scanPath}");
                continue;
            }

            // Recursive search for manifest.json files
            foreach (var manifestPath in Directory.EnumerateFiles(scanPath, "manifest.json", SearchOption.AllDirectories))
            {
                var packDir = Path.GetDirectoryName(manifestPath);
                if (packDir == null) continue;

                try
                {
                    var packData = await ParsePackAsync(packDir, manifestPath);
                    if (packData != null)
                        packs.Add(packData);
                }
                catch (JsonException jsonEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Invalid JSON in {manifestPath}: {jsonEx.Message}");
                    // Skip this pack - likely encrypted marketplace content
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error parsing pack {packDir}: {ex.Message}");
                }
            }
        }

        packs.Sort((a, b) => string.Compare(a.PackName, b.PackName, StringComparison.OrdinalIgnoreCase));
        return packs;
    }

    private async Task<PackData> ParsePackAsync(string packDir, string manifestPath)
    {
        var json = await File.ReadAllTextAsync(manifestPath);

        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        var root = doc.RootElement;

        // Extract UUIDs to check if this is a Vanilla RTX pack
        string headerUUID = null;
        string moduleUUID = null;

        if (root.TryGetProperty("header", out var header))
        {
            if (header.TryGetProperty("uuid", out var headerUuidElement))
                headerUUID = headerUuidElement.GetString();
        }

        if (root.TryGetProperty("modules", out var modules))
        {
            foreach (var module in modules.EnumerateArray())
            {
                if (module.TryGetProperty("uuid", out var moduleUuidElement))
                {
                    moduleUUID = moduleUuidElement.GetString();
                    break; // Get first module UUID
                }
            }
        }

        // Filter out Vanilla RTX packs
        if (!string.IsNullOrEmpty(headerUUID) && !string.IsNullOrEmpty(moduleUUID))
        {
            if ((string.Equals(headerUUID, PackLocator.VANILLA_RTX_HEADER_UUID, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(moduleUUID, PackLocator.VANILLA_RTX_MODULE_UUID, StringComparison.OrdinalIgnoreCase)) ||
                (string.Equals(headerUUID, PackLocator.VANILLA_RTX_NORMALS_HEADER_UUID, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(moduleUUID, PackLocator.VANILLA_RTX_NORMALS_MODULE_UUID, StringComparison.OrdinalIgnoreCase)) ||
                (string.Equals(headerUUID, PackLocator.VANILLA_RTX_OPUS_HEADER_UUID, StringComparison.OrdinalIgnoreCase) &&
                 string.Equals(moduleUUID, PackLocator.VANILLA_RTX_OPUS_MODULE_UUID, StringComparison.OrdinalIgnoreCase)))
            {
                System.Diagnostics.Debug.WriteLine($"Skipping Vanilla RTX pack: {packDir}");
                return null; // Skip this pack
            }
        }

        // Check capabilities
        var capabilityTags = new List<string>();

        if (root.TryGetProperty("capabilities", out var capabilities))
        {
            bool hasRaytraced = false;
            bool hasPbr = false;

            foreach (var cap in capabilities.EnumerateArray())
            {
                var capValue = cap.GetString();
                if (capValue != null)
                {
                    var capLower = capValue.ToLowerInvariant();
                    if (capLower == "raytraced" || capLower == "ray_traced")
                    {
                        hasRaytraced = true;
                    }
                    else if (capLower == "pbr")
                    {
                        hasPbr = true;
                    }
                }
            }

            if (hasRaytraced)
                capabilityTags.Add("RTX");
            if (hasPbr)
                capabilityTags.Add("Vibrant Visuals");
        }

        if (capabilityTags.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine($"Pack {packDir} has no compatible capabilities");
            return null;
        }

        // Get pack name and description
        string packName = "pack.name";
        string packDescription = "pack.description";

        if (header.TryGetProperty("name", out var nameElement))
        {
            var name = nameElement.GetString();
            if (!string.IsNullOrWhiteSpace(name))
                packName = name;
        }

        if (header.TryGetProperty("description", out var descElement))
        {
            var desc = descElement.GetString();
            if (!string.IsNullOrWhiteSpace(desc))
                packDescription = desc;
        }

        // If localization keys detected, try to load from lang files
        if (packName == "pack.name" || packDescription == "pack.description")
        {
            var langFolder = Path.Combine(packDir, "texts");
            if (Directory.Exists(langFolder))
            {
                var langData = await TryLoadLangFileAsync(langFolder);

                if (langData != null)
                {
                    if (packName == "pack.name" && langData.ContainsKey("pack.name"))
                        packName = langData["pack.name"];

                    if (packDescription == "pack.description" && langData.ContainsKey("pack.description"))
                        packDescription = langData["pack.description"];
                }
            }
        }

        // Fallback to directory name if still localized
        if (packName == "pack.name")
            packName = Path.GetFileName(packDir);
        if (packDescription == "pack.description")
            packDescription = "";

        var icon = await LoadIconAsync(packDir);

        return new PackData
        {
            PackName = packName,
            PackDescription = packDescription,
            PackPath = packDir,
            Icon = icon,
            CapabilityTags = capabilityTags
        };
    }

    private async Task<Dictionary<string, string>> TryLoadLangFileAsync(string langFolder)
    {
        if (!Directory.Exists(langFolder))
            return null;

        var langFiles = Directory.GetFiles(langFolder, "*.lang")
            .Where(f => Path.GetFileName(f).StartsWith("en", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var langPath in langFiles)
        {
            try
            {
                var langData = new Dictionary<string, string>();
                var lines = await File.ReadAllLinesAsync(langPath);

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                        continue;

                    var equalIndex = line.IndexOf('=');
                    if (equalIndex > 0)
                    {
                        var key = line.Substring(0, equalIndex).Trim();
                        var value = line.Substring(equalIndex + 1).Trim();
                        langData[key] = value;
                    }
                }

                if (langData.ContainsKey("pack.name") || langData.ContainsKey("pack.description"))
                    return langData;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading lang file {langPath}: {ex.Message}");
            }
        }

        return null;
    }

    private async Task<BitmapImage> LoadIconAsync(string packDir)
    {
        // Case-insensitive icon search
        var iconFiles = Directory.GetFiles(packDir, "pack_icon.*")
            .Where(f =>
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga";
            })
            .ToArray();

        foreach (var iconPath in iconFiles)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Loading icon: {iconPath}");
                var bitmap = new BitmapImage();

                using (var fileStream = File.OpenRead(iconPath))
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        await fileStream.CopyToAsync(memoryStream);
                        memoryStream.Position = 0;

                        var randomAccessStream = memoryStream.AsRandomAccessStream();
                        await bitmap.SetSourceAsync(randomAccessStream);
                    }
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading icon {iconPath}: {ex.Message}");
            }
        }

        return null;
    }

    private class PackData
    {
        public string PackName
        {
            get; set;
        }
        public string PackDescription
        {
            get; set;
        }
        public string PackPath
        {
            get; set;
        }
        public BitmapImage Icon
        {
            get; set;
        }
        public List<string> CapabilityTags
        {
            get; set;
        }
    }
}