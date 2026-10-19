# Protoss Mech : Persona Hero - Test Build

Feel free to download and play, but **please do not upload to the Steam Workshop or share the files directly.** If you want to share it, please send the link of this repository.

**Note before downloading:**
1. This is a test version; updates may be frequent and could change core mechanics.
2. Art assets (textures) may be missing or use placeholders.

**Requirement:**
RimWorld **1.7 only**, with Harmony, Biotech, [PandaLib](https://github.com/RedstonePanda00/PandaLib), [ProtossMech: Core](https://github.com/RedstonePanda00/ProtossMech_Purifier.git) and RimCampaign. Start a new game; 1.6 saves are not supported.

Source code is in `Source/PMP_Persona/PMP_Persona`; build output is written to `1.7/Assemblies`.

```powershell
dotnet build Source/PMP_Persona/PMP_Persona/PMP_Persona.csproj --configfile NuGet.Config -v:minimal
```

Runtime distribution consists of `About`, `1.7`, `Languages`, `Textures`, `LoadFolders.xml`, and `Sounds` when present. Keep source code and build intermediates out of the installed mod directory.
