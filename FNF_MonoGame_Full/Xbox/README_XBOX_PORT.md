# FNF MonoGame - Xbox Port

## Quick Start

### Step 1: Install Prerequisites
Run as **Administrator** in PowerShell:
```powershell
cd FNF_MonoGame_Full\Xbox
.\install_prerequisites.ps1
```
This installs the UWP workload + Windows 10 SDK via Visual Studio Installer.

**Or manually:**
1. Open **Visual Studio Installer**
2. Click **Modify** on your VS 2022 installation
3. Check **"Universal Windows Platform development"**
4. In Individual Components, check **"Xbox development tools"**
5. Click **Modify** to install (~3-5 GB)

### Step 2: Enable Xbox Dev Mode
1. On your Xbox, go to **Microsoft Store**
2. Search for **"Dev Mode Activation"** and install it
3. Open the app and follow the activation steps
4. Your Xbox will restart into **Developer Mode**
5. Note the **IP address** shown in **Dev Home** app on Xbox

### Step 3: Build the APPX
```powershell
cd FNF_MonoGame_Full\Xbox
.\build_xbox.ps1              # Debug build
.\build_xbox.ps1 -Release     # Release build (optimized)
```

### Step 4: Deploy to Xbox
**Option A: Command line**
```powershell
.\build_xbox.ps1 -Deploy -XboxIP 192.168.1.100
```

**Option B: Visual Studio (recommended)**
1. Open `Xbox\FNF_Xbox.csproj` in Visual Studio 2022
2. Set **Platform** to **x64**
3. Set **Debug Target** to **Remote Machine**
4. Enter your **Xbox IP address**
5. Press **F5** to build and deploy

**Option C: Xbox Device Portal**
1. Open browser to `https://YOUR_XBOX_IP:11443`
2. Accept the security certificate
3. Go to **My Games & Apps** ? **Add**
4. Upload the `.appx` file from `Xbox\AppxPackage\`

## Controller Mapping

### Menu Navigation
| Action    | Controller         | Keyboard         |
|-----------|--------------------|------------------|
| Navigate  | DPad / Left Stick  | Arrow Keys / WASD|
| Confirm   | A / Start          | Enter / Space    |
| Back      | B / Back           | Escape           |

### Gameplay Notes
| Note      | Controller              | Keyboard     |
|-----------|-------------------------|--------------|
| Left      | DPad Left / X / LT      | D / Left     |
| Down      | DPad Down / A / LB      | F / Down     |
| Up        | DPad Up / Y / RB        | J / Up       |
| Right     | DPad Right / B / RT     | K / Right    |
| Pause     | Start                   | Escape       |

**Smart button mode:** Face buttons (A/B/X/Y) automatically switch between
note input (during gameplay) and menu navigation (in menus/pause screen).

## Project Structure
```
Xbox/
??? FNF_Xbox.csproj          # UWP project (links source from parent)
??? Package.appxmanifest     # Xbox app manifest
??? App.xaml / App.xaml.cs   # UWP entry point
??? GamePage.xaml/.cs        # MonoGame SwapChainPanel host
??? XboxGame.cs              # Xbox-specific Game class
??? XboxAudioManager.cs      # NVorbis audio (replaces NAudio for UWP)
??? Properties/
?   ??? Default.rd.xml       # .NET Native runtime directives
??? Assets/                  # Store logos (replace with real art)
??? build_xbox.ps1           # Build script
??? install_prerequisites.ps1 # SDK installer
```

## Technical Notes

### Audio
The desktop version uses **NAudio** for OGG playback, which requires Win32 APIs
not available on UWP/Xbox. The Xbox version uses **NVorbis** (pure managed OGG
decoder) with MonoGame's `DynamicSoundEffectInstance` for streaming.

### Content
Game content (sprites, charts, audio) is shared between desktop and Xbox builds.
The Xbox project links to `../Content/` from the main project.

### Conditional Compilation
The Xbox project defines `XBOX_UWP` preprocessor symbol. Use this in shared code:
```csharp
#if XBOX_UWP
    // Xbox-specific code
#else
    // Desktop code
#endif
```

## Troubleshooting

**"Windows SDK not found"**
? Install VS 2022 with UWP workload, or download SDK from microsoft.com

**"Xbox not found"**
? Ensure Xbox is in Dev Mode and on the same network. Check IP in Dev Home app.

**"Deploy failed"**
? Check Xbox Device Portal (https://XBOX_IP:11443) is accessible. Try
  restarting Dev Mode on Xbox.

**Audio doesn't play on Xbox**
? Ensure all audio files are .ogg format (no .mp3). The Xbox audio manager
  only supports OGG via NVorbis.
