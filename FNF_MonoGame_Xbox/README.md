# FNF MonoGame Xbox Test

A simple Friday Night Funkin'-style rhythm game test built with MonoGame.
This project is designed to test Xbox deployment using NativeAOT-Xbox.

## Quick Test (Windows)

```batch
cd FNF_MonoGame_Xbox
dotnet run
```

Controls:
- **Arrow Keys** or **D-Pad**: Hit notes
- **Escape** or **Back Button**: Exit

## Xbox Deployment (via NativeAOT-Xbox)

### Prerequisites
1. Microsoft GDK installed
2. Xbox in Developer Mode
3. NativeAOT-Xbox repository cloned

### Steps

1. **Copy to NativeAOT-Xbox:**
   ```
   Copy this folder to: NativeAOT-Xbox/FNF_MonoGame_Xbox/
   ```

2. **Add to Solution:**
   - Open `NativeAOT-GDKX.sln`
   - Add existing project: `FNF_MonoGame_Xbox.csproj`
   - Set Bootstrap as startup project
   - Add FNF_MonoGame_Xbox as Bootstrap dependency

3. **Build:**
   - Select `Gaming.Xbox.Scarlett.x64` or `Gaming.Desktop.x64`
   - Build the solution
   - AOT output goes to `aot-output/`

4. **Deploy:**
   - Connect to your devkit
   - Run from Visual Studio

## Project Structure

```
FNF_MonoGame_Xbox/
??? Program.cs           # Entry point
??? FNFGame.cs          # Main game logic
??? FNF_MonoGame_Xbox.csproj
??? rd.xml              # NativeAOT reflection config
??? build_and_test.bat  # Build script
```

## Notes

- This is a TEST project to verify Xbox deployment works
- No external assets required (uses code-generated graphics)
- Full FNF would need proper asset loading and more features
