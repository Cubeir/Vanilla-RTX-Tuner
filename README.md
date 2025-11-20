# Vanilla-RTX-App

All-in-one Vanilla RTX app to tune various aspects of Vanilla RTX to your preferences, easily enable Minecraft's ray tracing, automatically update ray tracing packages for the latest versions of the game, and more...  
Ensuring ray tracing is accessible to newcomers, and frictionless for existing users — despite years of neglect from Mojang.

<!-- Microsoft Store badge -->
<p align="center">
  <a href="https://apps.microsoft.com/detail/9N6PCRZ5V9DJ?referrer=appbadge&mode=direct">
    <img src="https://get.microsoft.com/images/en-us%20dark.svg" width="300"/>
  </a>
</p>

<!-- Cover image -->
<p align="center">
  <img src="https://github.com/user-attachments/assets/5b95f170-a683-4c51-b175-4abd4c401a19" alt="vanilla-rtx-app-cover-render"/>
</p>

<!-- Badges -->
<p align="center">
  <a href="https://discord.gg/A4wv4wwYud">
    <img src="https://img.shields.io/discord/721377277480402985?style=flat-square&logo=discord&logoColor=F4E9D3&label=Discord&color=F4E9D3&cacheSeconds=3600"/>
  </a>
  <a href="https://ko-fi.com/cubeir">
    <img src="https://img.shields.io/badge/-support%20my%20work-F4E9D3?style=flat-square&logo=ko-fi&logoColor=F4E9D3&labelColor=555555"/>
  </a>
  <img src="https://img.shields.io/github/repo-size/Cubeir/Vanilla-RTX-App?style=flat-square&color=F4E9D3&label=Repo%20Size&cacheSeconds=3600"/>
  <img src="https://img.shields.io/github/last-commit/Cubeir/Vanilla-RTX-App?style=flat-square&color=F4E9D3&label=Last%20Commit&cacheSeconds=1800"/>
</p>


# Overview

Below you'll find an up-to-date list of features and documentation of functionalities included in the app.   

- `Launch Minecraft with RTX`  
  Launches Minecraft with ray tracing pre-enabled, additionally disables VSync for better performance and enables in-game graphics mode switching, this circumvents issues such as these that make enabling ray tracing difficult:    
  [MCPE-191513](https://bugs.mojang.com/browse/MCPE/issues/MCPE-191513): Ray tracing can no longer be enabled while in the main menu.  
  [MCPE-152158](https://bugs.mojang.com/browse/MCPE/issues/MCPE-152158): PBR textures don't load properly upon enabling ray tracing after the game is freshly launched.  
  [MCPE-121850](https://bugs.mojang.com/browse/MCPE/issues/MCPE-121850): Ray Tracing performance starvation when VSync is enabled.

- `Install latest RTX packages`  
  Downloads and (re)installs the latest Vanilla RTX & Vanilla RTX Normals packages directly from the [Vanilla RTX GitHub repository](https://github.com/cubeir/Vanilla-RTX)  
  No third party sites. Files are cached, making pack reinstalls instant unless a new update is available. This serves two main purposes: first to reset the packs back to original state, allowing you to rapidly experiment and learn in practice what the tuning options do, and second to ensure you're always getting the latest version of Vanilla RTX, acting as an auto-updater.

- `Preview (Toggle)`  
  All of the app's functionalities are targeted at Preview/Beta version of Minecraft instead of main release while  `Preview` is on.
  
- `Export selection`  
  Exports selected packs. Useful for backing up your snapshot of the pack before making further changes, or to share your tuned version of Vanilla RTX or another PBR resource pack with friends!
  
## Tuner

The Vanilla RTX App includes tools to tune Vanilla RTX or any other RTX or Vibrant Visuals resource pack according to your preferences. Tuning options use algorithms designed to protect the artist's original work with PBR textures, making it difficult to accidentally destroy the pack's intended look. The whole tuning process works together with the app's other features to keep the experience streamlined.

Upon launch, the app automatically scans for already-installed Vanilla RTX packs, available packs become selectable for tuning or export.    
If packs are installed or reinstalled through the app, or if Preview button is toggled, checkboxes refresh.
If multiple versions of the same Vanilla RTX variant are present, the highest will be picked, you can still select older versions manually from the list of local packs.

- `Select a local pack`  
  Opens a menu containing a list of your installed RTX or Vibrant Visuals resource packs. You can select one pack to be tuned alongside any of the 3 primary Vanilla RTX variants.
  Holding shift while pressing this button will instead trigger a Vanilla RTX version check which will be written in the sideebar logs.

- `Fog multiplier`  
  Updates all fog densities by a given factor — e.g., `0.5` to halve, `3.0` to triple, or `0` to effectively disable air fog. If a fog density is already at 0, the multiplier is instead converted into an acceptable literal number between `0.0-1.0`.
  If fog density is at maximum, excess of the multiplier will be used to scatter more light in the atmosphere. Underwater fog is affected partially to a much lesser degree.
  
  ![fog-panel](https://github.com/user-attachments/assets/a865a95c-f436-47f9-a56f-ec17c75e1fb0)

- `Emissivity multiplier`  
  Multiplies emissivity on blocks using a special formula that preserves the relative emissive values and keeps the composition intact, even if the multiplier is too high for a particular block.
  
  ![street-default-vanilla-rtx](https://github.com/user-attachments/assets/bc5af2b1-8dd3-47fc-8344-15bce477ba5d)
  ![street-3x-emissivity-tuned-vanilla-rtx](https://github.com/user-attachments/assets/a545d9c2-2890-46b3-b5f6-3cea7d98e13e)

- `Increase ambient light`  
Adds a small amount of emissivity to all surfaces, effectively increasing ambient light with ray tracing. With vibrant visuals this may result in a nightvision effect.  
This option works in conjunction with the Emissivity Multiplier — higher multipliers (e.g. 6.0) will amplify the effect.
Because changes stack on each tuning attempt, only use this once on freshly installed packs, and avoid setting higher emissivity multipliers on further consecutive tuning attempts.  
For that reason, `Emissivity Multiplier` is automatically reset to default (1.0) if previous tuning attempt has had this option enabled.

- `Normal intensity adjustment`  
  Adjusts normal map and heightmap intensities.
  A value of 0 will flatten the textures in any given PBR resource pack. Larger values will increase the intensity of normal maps on blocks  This is done through a special algorithm that makes it impossible to lose relative intensity data even with extreme values.

- `Material grain offset`  
  Creates grainy materials by adding a layer of noise, user input determines the maximum deviation.
  This is done safely with an algorithm that preserves pack's intended appearance while adding a layer of detail - emissives are affected minimally, and noise patterns persist across animated textures or texture variations (e.g. a redstone lamp off and redstone lamp on retain the same PBR noise)
  The noise is random with a subtle checkerboard pattern that mimics the noise on PBR textures seen in Vibrant Visuals, giving the pack a slightly fresh look each time noise is newly applied!
  
- `Roughen up`  
  Increases roughness on materials using a decaying curve function to impact glossy surfaces more than rough surfaces, allowing alignment with Vibrant Visuals' PBR artstyle.
  
- `Butcher heightmaps`  
  Uses a modified color texture to make the heightmaps less refined and lazier. The given number determines effectiveness `(0 = no change, 255 = fully lazy heightmaps)`.
  > Note: Assuming the pack isn't already shoddy, a value of 1-10 can be a nice choice!

- `Tune selection`  
  Begins the tuning process with your current settings and pack selections (Checked Vanilla RTX packages + one other local pack, if any was selected)
  Packages are then processed locally, as such changes you made are permanent, unless the pack is updated or freshly reinstalled.

> These tools can be extraordinarily powerful when used correctly on the right pack, for instance, PBR textures in Vanilla RTX can be processed to fully match the "style" of Mojang's Vibrant Visuals or most other vanilla PBR resource packs, **though this statement won't be true the other way around!**  

- `Clear Selection` 
  Clears the Vanilla RTX and custom pack selections, as if you just booted up the app!

- `Reset`  
  Resets tuning values and options to their defaults — this does not reset the pack back to its default state, to do that, you must reinstall the packages via `Reinstall latest RTX packs` or if it is a custom pack, manually reimport it to Minecraft.

- `Hard Reset (Shift Key + Reset)`  
  Wipes all of app's storage, as well as any temporary data, including: cache timestamps (update cooldowns), tuning options, app window state, cached pack location, cached app update files, and more..., then restarts the app.
  Effectively makes it as if the app was just freshly installed on your system!

## Miscellaneous

- Hovering any control in the app displays a unique Minecraft-inspired artwork communicating its function in the bottom left side of the app, for instance, sliders show how they should impact the textures in-game as you change them, toggles show before/after, and buttons display an artistic interpretation of what the they do!
  In combination with tooltips, this is meant to help make the app less intimidating and more beginner-friendly!  

- The following settings persist between sesssions: tuning options/slider values, Preview toggle, and theme choice.
  Allowing you to keep your personal favorite tuning values and quickly re-tune newer versions without having to remember everything.

- Top-left titlebar buttons in order:
  - Cycle themes: Change between dark, light, or system theme.
  - Help: Opens this page, which will hold up-to-date information about the app.
  - Donate: Opens the developer's Ko-Fi page.  
    When this button is hovered, an up-to-date list of Vanilla RTX Insiders is displayed. I'm able to maintain my projects thanks to them. Consider becoming a supporter to have your name there! (or else I have to resort to ads, dead serious!)

- The app may occasionally displays announcements from this readme file and caches it for several hours, these announcements will be shown on startup. They are used to inform users of important Vanilla RTX or App updates, known issues, etc... and sometimes for entertainment!

### Need help?

Join [Vanilla RTX server on Discord](https://discord.gg/A4wv4wwYud) & ask away!

### PSA
Bye Bye Tuner! The Vanilla RTX App has arrived with a fresh coat of paint and more capable than ever! Get the update from the Microsoft Store!
