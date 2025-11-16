# Vanilla-RTX-Tuner

All-in-one app to tune various aspects of Vanilla RTX to your preferences, automatically update Vanilla RTX ray tracing packages, launch Minecraft with RTX easily, and more...

⚠️ [Vanilla RTX +1.21.150 Only](https://github.com/Cubeir/Vanilla-RTX)  
⚠️ Requires Windows 10 20H1 (May 2020) or later


[![Discord](https://img.shields.io/discord/721377277480402985?style=flat-square&logo=discord&logoColor=F4E9D3&label=Discord&color=F4E9D3&cacheSeconds=3600)](https://discord.gg/A4wv4wwYud)
[![Ko-Fi](https://img.shields.io/badge/-support%20my%20work-F4E9D3?style=flat-square&logo=ko-fi&logoColor=F4E9D3&labelColor=555555)](https://ko-fi.com/cubeir)

![vanilla rtx tuner render 1 2](https://github.com/user-attachments/assets/fa30f80f-8863-4159-b53c-55797113974b)

# Overview

Below you'll find a comprehensive list of features and functionalities included in the latest version of the app.   

- **`Launch Minecraft RTX`**  
  Launches Minecraft with ray tracing pre-enabled by updating the game’s `options.txt` file. Additionally disables VSync for better performance, this is a direct workaround for the following issues:  
  [MCPE-191513](https://bugs.mojang.com/browse/MCPE/issues/MCPE-191513): Ray tracing can no longer be enabled while in the main menu.  
  [MCPE-152158](https://bugs.mojang.com/browse/MCPE/issues/MCPE-152158): PBR textures don't load properly upon enabling ray tracing after the game is freshly launched.  
  [MCPE-121850](https://bugs.mojang.com/browse/MCPE/issues/MCPE-121850): Ray Tracing performance starvation when VSync is enabled.

- **`Reinstall Latest Packages`**  
  Downloads and reinstalls the latest Vanilla RTX & Vanilla RTX Normals packages from the [Vanilla RTX GitHub repository](https://github.com/cubeir/Vanilla-RTX), redeploys from cache unless a new update is available.

- **`Export Selection`**  
  Exports selected packs. Useful for sharing tuned packs with friends or backing up your snapshot of the pack before making more changes.

- **Minecraft Preview support**  
  All of the app's functionality is targeted at Preview/Beta version of Minecraft while  `Preview` button is active.
## Tuning
- **`Locate Vanilla RTX`**  
  Locates all currently-installed Vanilla RTX versions. Present packs become selectable for tuning, exporting, etc...
  If multiple versions of the same pack are installed, only the most recent version will be picked up.

- **`Fog Multiplier`**  
  Updates all fog densities by a given number — e.g., `0.5` to halve, `3.0` to triple, or `0` to effectively disable air fog. If a fog density is already at 0, the multiplier is converted into an acceptable literal number between `0.0-1.0`.
  If fog density is at maximum, excess of the multiplier will be used to scatter more light in the atmosphere.
  
  ![fog-panel](https://github.com/user-attachments/assets/a865a95c-f436-47f9-a56f-ec17c75e1fb0)

- **`Emissivity Multiplier`**  
  Multiplies emissivity on blocks using a special formula that keeps the color composition intact, even when the multiplier is too high for a particular block.
  
  ![street-default-vanilla-rtx](https://github.com/user-attachments/assets/bc5af2b1-8dd3-47fc-8344-15bce477ba5d)
  ![street-3x-emissivity-tuned-vanilla-rtx](https://github.com/user-attachments/assets/a545d9c2-2890-46b3-b5f6-3cea7d98e13e)

- **`Increase Ambient Light`**  
Adds a small amount of emissivity to all surfaces, effectively increasing ambient light.
This works in conjunction with the Emissivity Multiplier — higher multipliers (e.g. 6.0) will amplify the effect.
Because changes stack on each tuning attempt, only use this once on freshly installed packs, and avoid setting higher emissivity multipliers on further consecutive tuning attempts.  
`Emissivity Multiplier` is automatically reset to default (1.0) if previous tuning attempt has had this option enabled.

- **`Normal Intensity Adjustment`**  
  Updates normal map intensity using a similar formula as the Emissive Multiplier, preserving relative intensity composition even at high percentage increases.

- **`Material Grain Offset`**  
  Creates grainy materials by adding a layer of noise, user input determines the maximum deviation.
  This is done in a safe manner with a special algorithm that makes it nearly impossible to take away from the pack's intended look.
  Additionally, noise patterns persist throughout animated textures.
  
- **`Roughen Up`**  
  Increases roughness on materials using a decaying curve function to impact glossy surfaces more than rough surfaces, allowing alignment with Vibrant Visuals' artstyle.
  
- **`Butcher Heightmaps`**  
  Uses a modified color texture to make the heightmaps less refined and more noisy. The given number determines effectiveness `(0 = no change, 255 = fully lazy heightmaps)`.

- **`Tune Selection`**  
  Begins the tuning process with your current settings. Packages are processed locally.
  Changes you make are permanent unless the pack is updated or freshly reinstalled.

- **`Reset`**  
  Resets tuning numbers and options to their defaults — this does not reset the pack back to its default state, to do that use `Reinstall Latest Packs` button

## Installation Guide

> **Prerequisites:**    
> 1. Requires [.NET 9.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0) for Windows (64-bit).   
> In most cases, it installs automatically with the `.msix`. If that is failing, try installing it manually from the link above.   
> 2. Windows [App Installer](https://learn.microsoft.com/en-us/windows/msix/app-installer/install-update-app-installer) to open `.msix` packages.  

### Option 1: Quick Install (Recommended)

1. [Download the latest package](https://github.com/Cubeir/Vanilla-RTX-Tuner/releases) from releases and unzip the `Vanilla.RTX.Tuner.WinUI_[version].zip` file.
2. **Right-click** `Installer.bat` and select **Run as administrator**.
3. The script will:
   - Automatically install the required certificate to the Trusted People store (Local Machine).
   - Launch the `.msix` package for installation. Once here, in the Windows App Installer click `Install`.

> Future `.msix` packages signed by Cubeir should remain trusted, allowing you to open the `.msix` directly.

### Option 2: Manual Certificate Import

1. Download and extract the latest release `.zip` package from [here](https://github.com/Cubeir/Vanilla-RTX-Tuner/releases).
2. Open the accompanying certificate file.
3. Click **Install Certificate**.
4. Select **Local Machine** > Next.
5. Choose **Place all certificates in the following store**, click **Browse**.
6. Select **Trusted People**, then OK.
7. Click Next > Finish.
8. You can now install the .msix package.

### Option 3: Build It Yourself

1. Clone this repository or download the source code.
2. Open the solution in Visual Studio 2022.
3. Enable Developer Mode in Windows (if not already).
4. Build — the app should auto-install. 

### Need help?

Join [Vanilla RTX server on Discord](https://discord.gg/A4wv4wwYud) & ask away!

### PSA
Vanilla RTX Tuner is dead! Long live Tuner! -- Kidding! The app is being renamed soon with a gigantic 2.0 release, stay tuned!
