using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;

namespace Vanilla_RTX_Tuner_WinUI.Modules;
public class Previewer
{
    private static Previewer _instance;
    private static readonly object _lock = new object();

    private Image _topVessel;
    private Image _bottomVessel;
    private bool _mouseDown = false;
    private FrameworkElement _activeControl = null;

    // Image caching to prevent flicker
    private string _currentBottomImage = "";
    private string _currentTopImage = "";

    // Cross-fade animation system
    private Storyboard _currentTransition = null;
    private int _transitionId = 0;
    private bool _isTransitioning = false;
    private bool _forceTransitionForControlChange = false;

    // Pending state for immediate application  after transition
    private class PendingVesselState
    {
        public string BottomImagePath
        {
            get; set;
        }
        public string TopImagePath
        {
            get; set;
        }
        public double BottomOpacity { get; set; } = 1.0;
        public double TopOpacity { get; set; } = 0.0;
        public bool HasPendingState { get; set; } = false;
    }

    private readonly PendingVesselState _pendingState = new();

    // Singleton instance access
    public static Previewer Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        throw new InvalidOperationException("Previews must be initialized first by calling Initialize()");
                    }
                }
            }
            return _instance;
        }
    }

    // Private constructor
    private Previewer(Image topVessel, Image bottomVessel)
    {
        _topVessel = topVessel;
        _bottomVessel = bottomVessel;

        _topVessel.Opacity = 0.0;
        _bottomVessel.Opacity = 0.0;
    }

    // Initialize the singleton instance
    public static void Initialize(Image topVessel, Image bottomVessel)
    {
        if (_instance == null)
        {
            lock (_lock)
            {
                if (_instance == null)
                {
                    _instance = new Previewer(topVessel, bottomVessel);
                }
            }
        }
    }

    // - - - - - Utility
    public void SetImages(string imageOnPath, string imageOffPath, bool useSmoothTransition = false)
    {
        double bottomOpacity = !string.IsNullOrEmpty(imageOffPath) ? 1.0 : 0.0;
        double topOpacity = !string.IsNullOrEmpty(imageOnPath) ? 1.0 : 0.0;
        SetVesselState(imageOffPath ?? "", imageOnPath ?? "", bottomOpacity, topOpacity, useSmoothTransition);
    }

    public void ClearPreviews()
    {
        _currentBottomImage = "";
        _currentTopImage = "";
        FadeVesselsOpacity(0.0, true);
        _bottomVessel.Source = null;
        _topVessel.Source = null;
    }

    public void FadeVesselsOpacity(double targetOpacity, bool useSmoothTransition = true)
    {
        targetOpacity = Math.Clamp(targetOpacity, 0.0, 1.0);

        if (useSmoothTransition)
        {
            var fadeTop = new DoubleAnimation
            {
                From = _topVessel.Opacity,
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(55)
            };

            var fadeBottom = new DoubleAnimation
            {
                From = _bottomVessel.Opacity,
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(55)
            };

            var storyboard = new Storyboard();
            Storyboard.SetTarget(fadeTop, _topVessel);
            Storyboard.SetTargetProperty(fadeTop, "Opacity");
            Storyboard.SetTarget(fadeBottom, _bottomVessel);
            Storyboard.SetTargetProperty(fadeBottom, "Opacity");

            storyboard.Children.Add(fadeTop);
            storyboard.Children.Add(fadeBottom);

            _currentTransition?.Stop();
            _currentTransition = storyboard;
            _isTransitioning = true;

            storyboard.Completed += (s, e) =>
            {
                _isTransitioning = false;
                _currentTransition = null;
            };

            storyboard.Begin();
        }
        else
        {
            _topVessel.Opacity = targetOpacity;
            _bottomVessel.Opacity = targetOpacity;
        }
    }
    // - - - - - Utility

    public void InitializeSlider(Slider slider, string defaultImagePath, string minImagePath, string maxImagePath, double defaultValue)
    {
        slider.SetValue(FrameworkElement.TagProperty, new SliderPreviewData
        {
            DefaultImagePath = defaultImagePath,
            MinImagePath = minImagePath,
            MaxImagePath = maxImagePath,
            DefaultValue = defaultValue
        });

        slider.ValueChanged += (s, e) => UpdateSliderPreview(slider);

        slider.PointerEntered += (s, e) =>
        {
            if (!_mouseDown || _activeControl == slider)
            {
                HandleControlChange(slider);
                UpdateSliderPreview(slider);
            }
        };

        slider.PointerPressed += (s, e) =>
        {
            _mouseDown = true;
            _activeControl = slider;
            slider.CapturePointer(e.Pointer);
            UpdateSliderPreview(slider);
        };

        slider.PointerReleased += (s, e) =>
        {
            _mouseDown = false;
            slider.ReleasePointerCapture(e.Pointer);
        };

        slider.PointerExited += (s, e) => { };

        slider.PointerCaptureLost += (s, e) =>
        {
            if (_mouseDown && _activeControl == slider)
            {
                _mouseDown = false;
                _activeControl = null;
            }
        };
    }

    public void InitializeButton(Button button, string imageOffPath = null, string imageOnPath = null)
    {
        button.SetValue(FrameworkElement.TagProperty, new ButtonPreviewData
        {
            ImageOnPath = imageOnPath,
            ImageOffPath = imageOffPath
        });

        button.PointerEntered += (s, e) =>
        {
            if (!_mouseDown || _activeControl == button)
            {
                HandleControlChange(button);
                SetButtonPreview(button, false);
            }
        };

        button.PointerPressed += (s, e) =>
        {
            _mouseDown = true;
            _activeControl = button;
            button.CapturePointer(e.Pointer);
            SetButtonPreview(button, true);
        };

        button.PointerReleased += (s, e) =>
        {
            _mouseDown = false;
            button.ReleasePointerCapture(e.Pointer);
            SetButtonPreview(button, false);
        };

        button.PointerExited += (s, e) => { };

        button.PointerCaptureLost += (s, e) =>
        {
            if (_mouseDown && _activeControl == button)
            {
                _mouseDown = false;
                _activeControl = null;
            }
        };
    }

    public void InitializeToggleButton(ToggleButton toggleButton, string imageOnPath, string imageOffPath)
    {
        toggleButton.SetValue(FrameworkElement.TagProperty, new TogglePreviewData
        {
            ImageOnPath = imageOnPath,
            ImageOffPath = imageOffPath
        });

        // Don't initialize preview state immediately - let it show on first hover
        // This prevents images from showing up on app startup

        toggleButton.Checked += (s, e) => {
            // Only update if this control is currently active (being hovered/interacted with)
            if (_activeControl == toggleButton)
            {
                SetTogglePreview(toggleButton, true);
            }
        };

        toggleButton.Unchecked += (s, e) => {
            // Only update if this control is currently active (being hovered/interacted with)
            if (_activeControl == toggleButton)
            {
                SetTogglePreview(toggleButton, false);
            }
        };

        toggleButton.PointerEntered += (s, e) =>
        {
            if (!_mouseDown || _activeControl == toggleButton)
            {
                HandleControlChange(toggleButton);
                SetTogglePreview(toggleButton, toggleButton.IsChecked ?? false);
            }
        };

        toggleButton.PointerPressed += (s, e) =>
        {
            _mouseDown = true;
            _activeControl = toggleButton;
            toggleButton.CapturePointer(e.Pointer);
            bool currentState = toggleButton.IsChecked ?? false;
            SetTogglePreview(toggleButton, !currentState);
        };

        toggleButton.PointerReleased += (s, e) =>
        {
            _mouseDown = false;
            toggleButton.ReleasePointerCapture(e.Pointer);
        };

        toggleButton.PointerExited += (s, e) => { };

        toggleButton.PointerCaptureLost += (s, e) =>
        {
            if (_mouseDown && _activeControl == toggleButton)
            {
                _mouseDown = false;
                _activeControl = null;
            }
        };
    }

    public void InitializeToggleSwitch(ToggleSwitch toggleSwitch, string imageOnPath, string imageOffPath)
    {
        toggleSwitch.SetValue(FrameworkElement.TagProperty, new TogglePreviewData
        {
            ImageOnPath = imageOnPath,
            ImageOffPath = imageOffPath
        });

        // Don't initialize preview state immediately - let it show on first hover

        toggleSwitch.Toggled += (s, e) => {
            // Only update if this control is currently active (being hovered/interacted with)
            if (_activeControl == toggleSwitch)
            {
                SetTogglePreview(toggleSwitch, toggleSwitch.IsOn);
            }
        };

        toggleSwitch.PointerEntered += (s, e) =>
        {
            if (!_mouseDown || _activeControl == toggleSwitch)
            {
                HandleControlChange(toggleSwitch);
                SetTogglePreview(toggleSwitch, toggleSwitch.IsOn);
            }
        };

        toggleSwitch.PointerPressed += (s, e) =>
        {
            _mouseDown = true;
            _activeControl = toggleSwitch;
            toggleSwitch.CapturePointer(e.Pointer);
            // For ToggleSwitch, preview the opposite state when pressed
            SetTogglePreview(toggleSwitch, !toggleSwitch.IsOn);
        };

        toggleSwitch.PointerReleased += (s, e) =>
        {
            _mouseDown = false;
            toggleSwitch.ReleasePointerCapture(e.Pointer);
        };

        toggleSwitch.PointerExited += (s, e) => { };

        toggleSwitch.PointerCaptureLost += (s, e) =>
        {
            if (_mouseDown && _activeControl == toggleSwitch)
            {
                _mouseDown = false;
                _activeControl = null;
            }
        };
    }

    public void InitializeCheckBox(CheckBox checkBox, string imageOnPath, string imageOffPath)
    {
        checkBox.SetValue(FrameworkElement.TagProperty, new TogglePreviewData
        {
            ImageOnPath = imageOnPath,
            ImageOffPath = imageOffPath
        });

        // Don't initialize preview state immediately - let it show on first hover

        checkBox.Checked += (s, e) => {
            // Only update if this control is currently active (being hovered/interacted with)
            if (_activeControl == checkBox)
            {
                SetTogglePreview(checkBox, true);
            }
        };

        checkBox.Unchecked += (s, e) => {
            // Only update if this control is currently active (being hovered/interacted with)
            if (_activeControl == checkBox)
            {
                SetTogglePreview(checkBox, false);
            }
        };

        checkBox.PointerEntered += (s, e) =>
        {
            if (!_mouseDown || _activeControl == checkBox)
            {
                HandleControlChange(checkBox);
                SetTogglePreview(checkBox, checkBox.IsChecked ?? false);
            }
        };

        checkBox.PointerPressed += (s, e) =>
        {
            _mouseDown = true;
            _activeControl = checkBox;
            checkBox.CapturePointer(e.Pointer);
            bool currentState = checkBox.IsChecked ?? false;
            SetTogglePreview(checkBox, !currentState);
        };

        checkBox.PointerReleased += (s, e) =>
        {
            _mouseDown = false;
            checkBox.ReleasePointerCapture(e.Pointer);
        };

        checkBox.PointerExited += (s, e) => { };

        checkBox.PointerCaptureLost += (s, e) =>
        {
            if (_mouseDown && _activeControl == checkBox)
            {
                _mouseDown = false;
                _activeControl = null;
            }
        };
    }

    private void HandleControlChange(FrameworkElement newControl)
    {
        bool isControlChange = (_activeControl != newControl && _activeControl != null);
        _activeControl = newControl;

        if (isControlChange)
        {
            // Mark that we need a transition for control change
            _pendingState.HasPendingState = false; // Will be set by the calling method
            _forceTransitionForControlChange = true; // New flag to force smooth transitions
        }
    }

    private void UpdateSliderPreview(Slider slider)
    {
        var data = slider.GetValue(FrameworkElement.TagProperty) as SliderPreviewData;
        if (data == null) return;

        double currentValue = slider.Value;
        double minValue = slider.Minimum;
        double maxValue = slider.Maximum;
        double defaultValue = data.DefaultValue;

        // Determine target images and opacities
        string targetBottomImage = data.DefaultImagePath ?? "";
        double targetBottomOpacity = !string.IsNullOrEmpty(data.DefaultImagePath) ? 1.0 : 0.0;

        // Check if both min and max images are null - if so, set top vessel opacity to 0
        bool bothMinMaxNull = string.IsNullOrEmpty(data.MinImagePath) && string.IsNullOrEmpty(data.MaxImagePath);
        string targetTopImage = "";
        double targetTopOpacity = 0.0;

        if (!bothMinMaxNull)
        {
            // Top vessel: Dynamic image and opacity based on slider position
            if (currentValue >= defaultValue)
            {
                targetTopImage = data.MaxImagePath ?? "";
                if (!string.IsNullOrEmpty(targetTopImage))
                {
                    if (maxValue == defaultValue)
                    {
                        targetTopOpacity = 0.0;
                    }
                    else
                    {
                        double progress = (currentValue - defaultValue) / (maxValue - defaultValue);
                        targetTopOpacity = Math.Min(1.0, Math.Max(0.0, progress));
                    }
                }
            }
            else
            {
                targetTopImage = data.MinImagePath ?? "";
                if (!string.IsNullOrEmpty(targetTopImage))
                {
                    if (minValue == defaultValue)
                    {
                        targetTopOpacity = 0.0;
                    }
                    else
                    {
                        double progress = (defaultValue - currentValue) / (defaultValue - minValue);
                        targetTopOpacity = Math.Min(1.0, Math.Max(0.0, progress));
                    }
                }
            }
        }

        // Use main transition system for inter-control changes, direct updates for intra-control
        bool useTransition = _forceTransitionForControlChange;
        _forceTransitionForControlChange = false; // Reset the flag

        SetVesselState(targetBottomImage, targetTopImage, targetBottomOpacity, targetTopOpacity, useTransition);
    }

    private void SetButtonPreview(Button button, bool isPressed)
    {
        var data = button.GetValue(FrameworkElement.TagProperty) as ButtonPreviewData;
        if (data == null) return;

        string imagePath = isPressed ? data.ImageOnPath : data.ImageOffPath;

        // For buttons: bottom vessel shows the image, top vessel is always transparent
        // If no image path provided, set bottom vessel opacity to 0 as well
        double bottomOpacity = !string.IsNullOrEmpty(imagePath) ? 1.0 : 0.0;
        SetVesselState(imagePath ?? "", "", bottomOpacity, 0.0, true);
    }

    private void SetTogglePreview(FrameworkElement element, bool isOn)
    {
        var data = element.GetValue(FrameworkElement.TagProperty) as TogglePreviewData;
        if (data == null) return;

        // Handle cases where image paths might be null
        double bottomOpacity = !string.IsNullOrEmpty(data.ImageOffPath) ? 1.0 : 0.0;
        double topOpacity = isOn && !string.IsNullOrEmpty(data.ImageOnPath) ? 1.0 : 0.0;

        SetVesselState(data.ImageOffPath ?? "", data.ImageOnPath ?? "", bottomOpacity, topOpacity, true);
    }

    private void SetVesselState(string bottomImagePath, string topImagePath, double bottomOpacity, double topOpacity, bool allowTransition)
    {
        bool bottomImageChanged = !string.IsNullOrEmpty(bottomImagePath) && _currentBottomImage != bottomImagePath;
        bool topImageChanged = !string.IsNullOrEmpty(topImagePath) && _currentTopImage != topImagePath;
        bool bottomOpacityChanged = Math.Abs(_bottomVessel.Opacity - bottomOpacity) > 0.01;
        bool topOpacityChanged = Math.Abs(_topVessel.Opacity - topOpacity) > 0.01;

        // Need transition if images changed OR if we're going from/to zero opacity with smooth transitions enabled
        bool needsTransition = allowTransition && (bottomImageChanged || topImageChanged ||
            (bottomOpacityChanged && (_bottomVessel.Opacity == 0.0 || bottomOpacity == 0.0)) ||
            (topOpacityChanged && (_topVessel.Opacity == 0.0 || topOpacity == 0.0)));

        if (!needsTransition)
        {
            // Direct application - no transition needed
            ApplyVesselState(bottomImagePath, topImagePath, bottomOpacity, topOpacity);
            return;
        }

        // Store the target state
        _pendingState.BottomImagePath = bottomImagePath;
        _pendingState.TopImagePath = topImagePath;
        _pendingState.BottomOpacity = bottomOpacity;
        _pendingState.TopOpacity = topOpacity;
        _pendingState.HasPendingState = true;

        // If already transitioning, the current transition will pick up the new state
        if (_isTransitioning)
        {
            return;
        }

        StartCrossFadeTransition();
    }

    private void StartCrossFadeTransition()
    {
        if (_isTransitioning)
        {
            // Cancel current transition
            _currentTransition?.Stop();
        }

        _isTransitioning = true;
        int currentTransitionId = ++_transitionId;

        // Determine if we need to fade out first (for image changes) or can directly fade to target
        bool bottomImageChanging = !string.IsNullOrEmpty(_pendingState.BottomImagePath) && _currentBottomImage != _pendingState.BottomImagePath;
        bool topImageChanging = !string.IsNullOrEmpty(_pendingState.TopImagePath) && _currentTopImage != _pendingState.TopImagePath;

        if (bottomImageChanging || topImageChanging)
        {
            // Image is changing - fade out first, then apply new image and fade in
            var fadeOutTop = new DoubleAnimation
            {
                From = _topVessel.Opacity,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(40)
            };

            var fadeOutBottom = new DoubleAnimation
            {
                From = _bottomVessel.Opacity,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(40)
            };

            var storyboard = new Storyboard();
            Storyboard.SetTarget(fadeOutTop, _topVessel);
            Storyboard.SetTargetProperty(fadeOutTop, "Opacity");
            Storyboard.SetTarget(fadeOutBottom, _bottomVessel);
            Storyboard.SetTargetProperty(fadeOutBottom, "Opacity");

            storyboard.Children.Add(fadeOutTop);
            storyboard.Children.Add(fadeOutBottom);

            storyboard.Completed += (s, e) =>
            {
                // Check if this transition is still valid
                if (currentTransitionId != _transitionId || !_pendingState.HasPendingState)
                {
                    _isTransitioning = false;
                    return;
                }

                // Apply the images while vessels are faded out
                ApplyVesselState(_pendingState.BottomImagePath, _pendingState.TopImagePath, 0.0, 0.0);

                // Now fade in to target opacities
                FadeInToTargetOpacities(currentTransitionId);
            };

            _currentTransition = storyboard;
            storyboard.Begin();
        }
        else
        {
            // No image change - just animate opacity directly to target
            FadeToTargetOpacities(currentTransitionId);
        }
    }

    private void FadeInToTargetOpacities(int transitionId)
    {
        var fadeInTop = new DoubleAnimation
        {
            From = 0.0,
            To = _pendingState.TopOpacity,
            Duration = TimeSpan.FromMilliseconds(50)
        };

        var fadeInBottom = new DoubleAnimation
        {
            From = 0.0,
            To = _pendingState.BottomOpacity,
            Duration = TimeSpan.FromMilliseconds(50)
        };

        var storyboard = new Storyboard();
        Storyboard.SetTarget(fadeInTop, _topVessel);
        Storyboard.SetTargetProperty(fadeInTop, "Opacity");
        Storyboard.SetTarget(fadeInBottom, _bottomVessel);
        Storyboard.SetTargetProperty(fadeInBottom, "Opacity");

        storyboard.Children.Add(fadeInTop);
        storyboard.Children.Add(fadeInBottom);

        storyboard.Completed += (s, e) =>
        {
            if (transitionId == _transitionId)
            {
                _pendingState.HasPendingState = false;
                _isTransitioning = false;

                // Check if there's another pending state that came in during transition
                if (_pendingState.HasPendingState)
                {
                    StartCrossFadeTransition();
                }
            }
        };

        _currentTransition = storyboard;
        storyboard.Begin();
    }

    private void FadeToTargetOpacities(int transitionId)
    {
        var fadeTop = new DoubleAnimation
        {
            From = _topVessel.Opacity,
            To = _pendingState.TopOpacity,
            Duration = TimeSpan.FromMilliseconds(60)
        };

        var fadeBottom = new DoubleAnimation
        {
            From = _bottomVessel.Opacity,
            To = _pendingState.BottomOpacity,
            Duration = TimeSpan.FromMilliseconds(50)
        };

        var storyboard = new Storyboard();
        Storyboard.SetTarget(fadeTop, _topVessel);
        Storyboard.SetTargetProperty(fadeTop, "Opacity");
        Storyboard.SetTarget(fadeBottom, _bottomVessel);
        Storyboard.SetTargetProperty(fadeBottom, "Opacity");

        storyboard.Children.Add(fadeTop);
        storyboard.Children.Add(fadeBottom);

        storyboard.Completed += (s, e) =>
        {
            if (transitionId == _transitionId)
            {
                _pendingState.HasPendingState = false;
                _isTransitioning = false;

                // Check if there's another pending state that came in during transition
                if (_pendingState.HasPendingState)
                {
                    StartCrossFadeTransition();
                }
            }
        };

        _currentTransition = storyboard;
        storyboard.Begin();
    }

    private void ApplyVesselState(string bottomImagePath, string topImagePath, double bottomOpacity, double topOpacity)
    {
        // Apply bottom vessel
        if (!string.IsNullOrEmpty(bottomImagePath) && _currentBottomImage != bottomImagePath)
        {
            SetBottomVesselImage(bottomImagePath);
            _currentBottomImage = bottomImagePath;
        }

        // Apply top vessel
        if (!string.IsNullOrEmpty(topImagePath) && _currentTopImage != topImagePath)
        {
            SetTopVesselImage(topImagePath);
            _currentTopImage = topImagePath;
        }

        // Set opacities (will be animated by the calling transition if needed)
        _bottomVessel.Opacity = bottomOpacity;
        _topVessel.Opacity = topOpacity;
    }

    private void SetBottomVesselImage(string imagePath)
    {
        if (!string.IsNullOrEmpty(imagePath))
        {
            _bottomVessel.Source = new BitmapImage(new Uri(imagePath));
        }
    }

    private void SetTopVesselImage(string imagePath)
    {
        if (!string.IsNullOrEmpty(imagePath))
        {
            _topVessel.Source = new BitmapImage(new Uri(imagePath));
        }
    }
    private class SliderPreviewData
    {
        public string DefaultImagePath
        {
            get; set;
        }
        public string MinImagePath
        {
            get; set;
        }
        public string MaxImagePath
        {
            get; set;
        }
        public double DefaultValue
        {
            get; set;
        }
    }

    private class ButtonPreviewData
    {
        public string ImageOnPath
        {
            get; set;
        }
        public string ImageOffPath
        {
            get; set;
        }
    }

    private class TogglePreviewData
    {
        public string ImageOnPath
        {
            get; set;
        }
        public string ImageOffPath
        {
            get; set;
        }
    }
}