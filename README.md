# Vanilla-RTX-Tuner

An all-in-one app to automatically update Vanilla RTX, tune various aspects to your preferences, launch Minecraft RTX with ease, and more!

⚠️ [Vanilla RTX +1.21.150 Only](https://github.com/Cubeir/Vanilla-RTX)

[![Discord](https://img.shields.io/discord/721377277480402985?style=flat-square&logo=discord&logoColor=F4E9D3&label=Discord&color=F4E9D3&cacheSeconds=3600)](https://discord.gg/A4wv4wwYud)
[![Ko-Fi](https://img.shields.io/badge/-support%20my%20work💖-F4E9D3?style=flat-square&logo=ko-fi&logoColor=F4E9D3&labelColor=555555)](https://ko-fi.com/cubeir)

![vanilla-rtx-tuner-banner](https://github.com/user-attachments/assets/75ba9d74-e482-4934-a06f-f7db07992a15)

## Overview

Below you'll find a comprehensive list of features and functionalities included in the latest version of the app.   

![vanilla-rtx-tuner-render-1280x720](https://github.com/user-attachments/assets/1b2cf19e-22fb-4ad2-a4f3-1f124f7a3d52)



- **`Locate Vanilla RTX Projects`**  
  Locates all currently-installed Vanilla RTX versions. Present packs become selectable. Further changes and exporting affect only the selected packs.

- **Full Minecraft Preview support**  
  Toggle using the `Target Preview` button.

- **`Fog Multiplier`**  
  Updates all fog densities by a given number — e.g., `0.5` to halve, `3.0` to triple, or `0` to effectively disable air fog. If a fog density is already at 0, the multiplier is converted into an acceptable literal number between `0.0–1.0`.

- **`Emissivity Multiplier`**  
  Multiplies emissivity on blocks using a special formula that keeps the color composition intact, even when the multiplier is too high for a particular block. Typical use case: make all emissive textures brighter.

- **`Normal Intensity Adjustment`**  
  Updates normal map intensity using a similar formula as the Emissive Multiplier, preserving relative intensity composition even at high percentage increases.

- **`Material Noise Offset`**  
  Creates grainy materials by adding a layer of noise, user input determines the maximum deviation.
  This is done in a safe manner with a special algorithm that makes it nearly impossible to take away from the pack's intended look.

- **`Roughen Up`**  
  Increases roughness on materials using an inverse curve function to impact lower values more than higher ones to more closely match Vibrant Visuals' artstyle.

- **`Butcher Heightmaps`**  
  Uses the colormap to make the heightmaps less refined and more noisy. The given number determines effectiveness (`0 = no change`, `255 = fully lazy heightmaps`).

- **`Tune Selection`**  
  Begins the tuning process. Packages are processed locally. Changes are permanent unless the pack is updated or freshly reinstalled.

- **`Reinstall Latest Packages`**  
  Downloads and reinstalls the latest Vanilla RTX & Vanilla RTX Normals packages from their GitHub repository.

- **`Export Selection`**  
  Exports selected packs. Useful for sharing your tuned packs with friends or backing up before making more changes.

- **`Launch Minecraft RTX`**  
  Launches Minecraft with ray tracing pre-enabled by updating the game’s `options.txt` file. Additionally disables VSync for better performance.

## Installation Guide

There are 3 options:

### Option 1: Quick Install (Recommended)

1. Download and extract the latest `.zip` package.
2. **Right-click** `Installer.bat` and select **Run as administrator**.
3. The script will:
   - Automatically install the required certificate to the Trusted People store (Local Machine).
   - Launch the `.msix` package for installation.

You’ll be prompted with the MSIX installer UI.  
Future `.msix` packages signed by Cubeir will be be automatically trusted, allowing you to open the `.msix` directly.


### Option 2: Manual Certificate Import

1. Download and extract the latest `.zip` package.
2. Open the accompanying certificate file.
3. Click **Install Certificate**.
4. Select **Local Machine** > Next.
5. Choose **Place all certificates in the following store**, click **Browse**.
6. Select **Trusted People**, then OK.
7. Click Next > Finish.

You can now install the .msix package.

### Option 3: Build It Yourself

1. Clone this repository.
2. Open the solution in Visual Studio 2022.
3. Enable Developer Mode in Windows (if not already).
4. Build — the app should auto-install.


## Need help?

Join the Vanilla RTX community on Discord: [https://discord.gg/A4wv4wwYud](https://discord.gg/A4wv4wwYud)
