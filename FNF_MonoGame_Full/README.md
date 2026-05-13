# Friday Night Funkin' - MonoGame Port

A full port of Friday Night Funkin' to MonoGame/XNA for cross-platform support including Xbox.

## Project Structure

```
FNF_MonoGame_Full/
??? Program.cs                    # Entry point
??? FNFGame.cs                   # Main game class
??? Engine/
?   ??? AssetManager.cs          # Texture/sound loading
?   ??? AudioManager.cs          # Music and SFX
?   ??? InputManager.cs          # Keyboard/gamepad input
?   ??? SceneManager.cs          # Scene transitions
??? Scenes/
?   ??? TitleScene.cs            # Title screen
?   ??? MainMenuScene.cs         # Main menu
?   ??? SongSelectScene.cs       # Song selection
?   ??? PlayScene.cs             # Gameplay
?   ??? ResultsScene.cs          # End of song results
??? Gameplay/
?   ??? Chart.cs                 # Song chart loading
?   ??? Conductor.cs             # Beat/timing sync
?   ??? NoteField.cs             # Note management
?   ??? Character.cs             # Character animations
??? Content/                     # Game assets (copied from Godot)
    ??? menus/
    ??? game/
    ??? fonts/
    ??? songs/
    ??? resources/
```

## Quick Start

### 1. Copy Assets
Run `copy_assets.bat` to copy all art/music from the Godot FNF project.

### 2. Build and Run
```batch
dotnet restore
dotnet run
```

Or run `build_and_run.bat`

## Controls

### Keyboard
| Key | Action |
|-----|--------|
| A / ? | Left Arrow |
| S / ? | Down Arrow |
| W / ? | Up Arrow |
| D / ? | Right Arrow |
| Enter | Confirm |
| Escape | Back/Pause |

### Controller (Xbox)
| Button | Action |
|--------|--------|
| D-Pad | Hit Notes |
| A | Confirm |
| B | Back |
| Start | Pause |

## Xbox Deployment

This project is designed to work with NativeAOT-Xbox for Xbox console deployment.

### Requirements
- Microsoft GDK (requires Xbox Partner Program access)
- NativeAOT-Xbox repository

### Steps
1. Copy project to NativeAOT-Xbox folder
2. Add to NativeAOT-GDKX.sln
3. Build for Gaming.Xbox.Scarlett.x64
4. Deploy via Xbox Device Portal

## Asset Notes

- **PNG textures** load directly (no conversion needed)
- **OGG audio** requires NVorbis or conversion to WAV
- **JSON charts** are parsed using Newtonsoft.Json
- **Godot .import files** are ignored

## TODO

- [ ] Full spritesheet animation support
- [ ] OGG audio streaming with NVorbis
- [ ] Character JSON loading
- [ ] Stage loading
- [ ] Settings/options menu
- [ ] Save data
- [ ] Mods support
