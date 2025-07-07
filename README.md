# Vanilla-RTX-Tuner

An all-in-one app to tune various aspects of Vanilla RTX to your preferences, automatically update packages & launch Minecraft RTX with ease, and more!

⚠️ [Vanilla RTX +1.21.150 Only](https://github.com/Cubeir/Vanilla-RTX)

[![Discord](https://img.shields.io/discord/721377277480402985?style=flat-square&logo=discord&logoColor=F4E9D3&label=Discord&color=F4E9D3&cacheSeconds=3600)](https://discord.gg/A4wv4wwYud)
[![Ko-Fi](https://img.shields.io/badge/-support%20my%20work💖-F4E9D3?style=flat-square&logo=ko-fi&logoColor=F4E9D3&labelColor=555555)](https://ko-fi.com/cubeir)

![vanilla-rtx-tuner-banner](https://github.com/user-attachments/assets/75ba9d74-e482-4934-a06f-f7db07992a15)

## Overview

Below you'll find a comprehensive list of features and functionalities included in the latest version of the app.   

![vanilla-rtx-tuner-render-1280x720](https://github.com/user-attachments/assets/1b2cf19e-22fb-4ad2-a4f3-1f124f7a3d52)



- **`Locate Vanilla RTX Projects`**  
  Locates all currently-installed Vanilla RTX versions. Installed packs become selectable. Further changes and exporting affect only the selected packs.

- **Minecraft Preview support**  
  All of the app's functionality is targeted at Preview version of the game while  `Target Preview` is active.

- **`Fog Multiplier`**  
  Updates all fog densities by a given number — e.g., `0.5` to halve, `3.0` to triple, or `0` to effectively disable air fog. If a fog density is already at 0, the multiplier is converted into an acceptable literal number between `0.0–1.0`.
  
  ![fog-panel](https://github.com/user-attachments/assets/a865a95c-f436-47f9-a56f-ec17c75e1fb0)

- **`Emissivity Multiplier`**  
  Multiplies emissivity on blocks using a special formula that keeps the color composition intact, even when the multiplier is too high for a particular block.
  
  ![street-default-vanilla-rtx](https://github.com/user-attachments/assets/bc5af2b1-8dd3-47fc-8344-15bce477ba5d)
  ![street-3x-emissivity-tuned-vanilla-rtx](https://github.com/user-attachments/assets/a545d9c2-2890-46b3-b5f6-3cea7d98e13e)

- **`Normal Intensity Adjustment`**  
  Updates normal map intensity using a similar formula as the Emissive Multiplier, preserving relative intensity composition even at high percentage increases.

- **`Material Noise Offset`**  
  Creates grainy materials by adding a layer of noise, user input determines the maximum deviation.
  This is done in a safe manner with a special algorithm that makes it nearly impossible to take away from the pack's intended look.
  
  ![grain-panel](https://github.com/user-attachments/assets/34af1221-8649-4976-80cf-d013cf21fa38)

- **`Roughen Up`**  
  Increases roughness on materials using an inverse curve function to impact lower values more than higher ones to more closely match Vibrant Visuals' artstyle.
  
  ![roughenup-slider](https://github.com/user-attachments/assets/fa365641-ec26-4a51-b519-c25c6af33843)

- **`Butcher Heightmaps`**  
  Uses a modified color texture to make the heightmaps less refined and more noisy. The given number determines effectiveness (0 = no change`, `255 = fully lazy heightmaps).

- **`Tune Selection`**  
  Begins the tuning process with your current settings. Packages are processed locally.
  Changes you make are permanent unless the pack is updated or freshly reinstalled.

- **`Reinstall Latest Packages`**  
  Downloads and reinstalls the latest Vanilla RTX & Vanilla RTX Normals packages from [this](https://github.com/cubeir/Vanilla-RTX) GitHub repository.

- **`Export Selection`**  
  Exports selected packs. Useful for sharing your tuned packs with friends or backing up before making more changes.

- **`Launch Minecraft RTX`**  
  Launches Minecraft with ray tracing pre-enabled by updating the game’s `options.txt` file. Additionally disables VSync for better performance. Direct workaround for:  
  [MCPE-191513](https://bugs.mojang.com/browse/MCPE/issues/MCPE-191513): Ray tracing can no longer be enabled while in the main menu.  
  [MCPE-152158](https://bugs.mojang.com/browse/MCPE/issues/MCPE-153053): PBR textures don't load properly upon enabling ray tracing after the game is freshly launched.  
  [MCPE-121850](https://bugs.mojang.com/browse/MCPE/issues/MCPE-121850): Ray Tracing performance starvation when VSync is enabled.  


## Installation Guide

> **Note:** Requires [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) for Windows (64-bit).  
> In most cases, it installs automatically with the `.msix`. If that fails, just install it manually from the link above.

### Option 1: Quick Install (Recommended)

1. [Download the latest package](https://github.com/Cubeir/Vanilla-RTX-Tuner/releases) from releases and unzip the `Vanilla.RTX.Tuner.WinUI_[version].zip` file.
2. **Right-click** `Installer.bat` and select **Run as administrator**.
3. The script will:
   - Automatically install the required certificate to the Trusted People store (Local Machine).
   - Launch the `.msix` package for installation. Once here, in the windows app installer click `Install`.

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

Join the Vanilla RTX community on Discord & ask away! [https://discord.gg/A4wv4wwYud](https://discord.gg/A4wv4wwYud)

### Want to help?

The scope of this project as it stands is small, but contributions are welcome.
You’ll find TODOs scattered in the code if you’re looking for a place to start.

## Known Issues
- Initial window size may be off with windows scaling setting other than 100%; resize manually.
- The app doesn't adapt if windows text size is set too large.
- The app's window may restore to a non-existent place and become invisible if the monitor it was previously closed on is turned off or unplugged; Temp solution: Reset app's data.
- Grainy materials produced by Material Noise Offset slider don't carry over the same noise to textures (e.g. currently noise varies between redstone lamp off and on)
