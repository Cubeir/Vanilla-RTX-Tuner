﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Vanilla_RTX_Tuner_WinUI;

/// <summary>
/// Universal control toggle utility for WinUI 3 applications
/// Manages enabling/disabling of controls while preserving original states
/// </summary>
public class WindowControlsManager
{
    // Global exclusion list - controls with these names are always ignored
    private static readonly HashSet<string> _globalExclusions = new()
        {
            "HelpButton",
            "SidebarLog",
            "SidelogProgressBar"
        };

    // Static dictionary to track states for multiple windows
    private static readonly Dictionary<Window, Dictionary<Control, bool>> _windowStates = new();

    /// <summary>
    /// Toggles all supported controls within the specified window
    /// </summary>
    /// <param name="window">The window containing controls to toggle</param>
    /// <param name="enable">True to restore original states, false to disable all</param>
    /// <param name="excludeNames">Optional list of control names to exclude from toggling</param>
    public static void ToggleControls(Window window, bool enable, params string[] excludeNames)
    {
        if (window?.Content == null) return;

        EnsureWindowStateExists(window);
        var states = _windowStates[window];
        var exclusions = new HashSet<string>(_globalExclusions);

        // Add any additional exclusions
        if (excludeNames != null)
        {
            foreach (var name in excludeNames)
            {
                if (!string.IsNullOrEmpty(name))
                    exclusions.Add(name);
            }
        }

        if (enable)
        {
            RestoreControlStates(states);
        }
        else
        {
            StoreAndDisableControls(window, states, exclusions);
        }
    }

    /// <summary>
    /// Adds a control name to the global exclusion list
    /// </summary>
    /// <param name="controlName">Name of the control to always exclude</param>
    public static void AddGlobalExclusion(string controlName)
    {
        if (!string.IsNullOrEmpty(controlName))
        {
            _globalExclusions.Add(controlName);
        }
    }

    /// <summary>
    /// Removes a control name from the global exclusion list
    /// </summary>
    /// <param name="controlName">Name of the control to remove from exclusions</param>
    public static void RemoveGlobalExclusion(string controlName)
    {
        if (!string.IsNullOrEmpty(controlName))
        {
            _globalExclusions.Remove(controlName);
        }
    }

    /// <summary>
    /// Clears all global exclusions
    /// </summary>
    public static void ClearGlobalExclusions()
    {
        _globalExclusions.Clear();
    }

    /// <summary>
    /// Clears stored states for a window (useful for cleanup)
    /// </summary>
    /// <param name="window">The window to clear states for</param>
    public static void ClearStates(Window window)
    {
        if (window != null && _windowStates.ContainsKey(window))
        {
            _windowStates[window].Clear();
            _windowStates.Remove(window);
        }
    }

    private static void EnsureWindowStateExists(Window window)
    {
        if (!_windowStates.ContainsKey(window))
        {
            _windowStates[window] = new Dictionary<Control, bool>();
        }
    }

    private static void StoreAndDisableControls(Window window, Dictionary<Control, bool> states, HashSet<string> exclusions)
    {
        states.Clear();
        var controls = GetAllSupportedControls(window.Content, exclusions);

        foreach (var control in controls)
        {
            states[control] = control.IsEnabled;
            control.IsEnabled = false;
        }
    }

    private static void RestoreControlStates(Dictionary<Control, bool> states)
    {
        foreach (var kvp in states)
        {
            kvp.Key.IsEnabled = kvp.Value;
        }
        states.Clear();
    }

    private static IEnumerable<Control> GetAllSupportedControls(DependencyObject parent, HashSet<string> exclusions = null)
    {
        if (parent == null) yield break;

        var childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);

        for (var i = 0; i < childCount; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);

            if (IsSupportedControl(child))
            {
                var control = (Control)child;

                // Check if this control should be excluded
                if (exclusions == null || !exclusions.Contains(control.Name))
                {
                    yield return control;
                }
            }

            foreach (var grandChild in GetAllSupportedControls(child, exclusions))
            {
                yield return grandChild;
            }
        }
    }

    private static bool IsSupportedControl(DependencyObject control)
    {
        return control is Button ||
               control is CheckBox ||
               control is RadioButton ||
               control is Slider ||
               control is TextBox ||
               control is PasswordBox ||
               control is ComboBox ||
               control is ListBox ||
               control is ListView ||
               control is ToggleButton ||
               control is RatingControl ||
               control is NumberBox ||
               control is DatePicker ||
               control is TimePicker ||
               control is ToggleSwitch ||
               control is MenuFlyoutItem ||
               control is AppBarButton ||
               control is AppBarToggleButton ||
               control is AutoSuggestBox;
    }
}

/// <summary>
/// Extension methods for convenient usage
/// </summary>
public static class WindowControlsManagerExtensions
{
    /// <summary>
    /// Toggles all controls in this window
    /// </summary>
    /// <param name="window">The window to toggle controls for</param>
    /// <param name="enable">True to restore, false to disable</param>
    /// <param name="excludeNames">Optional list of control names to exclude</param>
    public static void ToggleControls(this Window window, bool enable, params string[] excludeNames)
    {
        WindowControlsManager.ToggleControls(window, enable, excludeNames);
    }

    /// <summary>
    /// Disables all controls in this window
    /// </summary>
    /// <param name="window">The window to disable controls for</param>
    /// <param name="excludeNames">Optional list of control names to exclude</param>
    public static void DisableAllControls(this Window window, params string[] excludeNames)
    {
        WindowControlsManager.ToggleControls(window, false, excludeNames);
    }

    /// <summary>
    /// Restores all controls in this window to their original states
    /// </summary>
    /// <param name="window">The window to restore controls for</param>
    public static void RestoreAllControls(this Window window)
    {
        WindowControlsManager.ToggleControls(window, true);
    }

    /// <summary>
    /// Clears stored control states for this window
    /// </summary>
    /// <param name="window">The window to clear states for</param>
    public static void ClearControlStates(this Window window)
    {
        WindowControlsManager.ClearStates(window);
    }
}