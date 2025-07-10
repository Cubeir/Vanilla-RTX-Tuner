using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

// TODO: Do something for if user unplugs monitor -- currently it causes that annoying case of window opening in a non-existent position?
public class WindowStateManager
{
    private AppWindow _appWindow;
    private Window _window;
    private Action<string> _logAction;

    public WindowStateManager(Window window, Action<string> logAction = null)
    {
        _window = window;
        _logAction = logAction;

        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        _appWindow.Changed += OnAppWindowChanged;
        _window.Closed += OnWindowClosed;
    }

    public void ApplySavedStateOrDefaults(SizeInt32 defaultSize, SizeInt32? defaultPosition = null)
    {
        var savedState = LoadWindowState();

        if (savedState.HasValue)
        {
            _appWindow.Resize(new SizeInt32(savedState.Value.Width, savedState.Value.Height));
            _appWindow.Move(new PointInt32(savedState.Value.X, savedState.Value.Y));
        }
        else
        {
            // fallback defaults, or center the window
            _appWindow.Resize(defaultSize);

            if (defaultPosition.HasValue)
            {
                _appWindow.Move(new PointInt32(defaultPosition.Value.Width, defaultPosition.Value.Height));
            }
            else
            {
                CenterWindow(defaultSize);
            }
        }
    }


    // previously used to center window all of the time
    private void CenterWindow(SizeInt32 windowSize)
    {
        var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var centerX = displayArea.WorkArea.X + (displayArea.WorkArea.Width - windowSize.Width) / 2;
        var centerY = displayArea.WorkArea.Y + (displayArea.WorkArea.Height - windowSize.Height) / 2;
        _appWindow.Move(new PointInt32(centerX, centerY));
    }

    private struct WindowState
    {
        public int X
        {
            get; set;
        }
        public int Y
        {
            get; set;
        }
        public int Width
        {
            get; set;
        }
        public int Height
        {
            get; set;
        }
    }

    private WindowState? LoadWindowState()
    {
        try
        {
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
            var values = settings.Values;

            if (values.ContainsKey("WindowX") && values.ContainsKey("WindowY") &&
                values.ContainsKey("WindowWidth") && values.ContainsKey("WindowHeight"))
            {
                return new WindowState
                {
                    X = (int)values["WindowX"],
                    Y = (int)values["WindowY"],
                    Width = (int)values["WindowWidth"],
                    Height = (int)values["WindowHeight"]
                };
            }
        }
        catch (Exception ex)
        {
            _logAction?.Invoke($"Failed to load window state: {ex.Message}");
        }

        return null;
    }

    private void SaveWindowState()
    {
        try
        {
            if (_appWindow == null) return;

            var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
            var values = settings.Values;

            values["WindowX"] = _appWindow.Position.X;
            values["WindowY"] = _appWindow.Position.Y;
            values["WindowWidth"] = _appWindow.Size.Width;
            values["WindowHeight"] = _appWindow.Size.Height;
        }
        catch (Exception ex)
        {
            _logAction?.Invoke($"Failed to save window state: {ex.Message}");
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange || args.DidPositionChange)
        {
            SaveWindowState();
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        SaveWindowState();

        // Cleanup
        if (_appWindow != null)
        {
            _appWindow.Changed -= OnAppWindowChanged;
        }
        if (_window != null)
        {
            _window.Closed -= OnWindowClosed;
        }
    }
}