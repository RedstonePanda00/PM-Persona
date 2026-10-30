# Protoss Mech : Persona Hero - Test Build

Feel free to download and play, but **please do not upload to the Steam Workshop or share the files directly.** If you want to share it, please send the link of this repository.

**Note before downloading:**
1. This is a test version; updates may be frequent and could change core mechanics.
2. Art assets (textures) may be missing or use placeholders.

**Campaign files:** Maps use losslessly compressed JSON (`.json.gz`). Use the updated RimCampaign framework to open and play them; Git LFS is not required.

**Requirement:**
RimWorld **1.7 only**, with Harmony, Biotech, [PandaLib](https://github.com/RedstonePanda00/PandaLib), [ProtossMech: Core](https://github.com/RedstonePanda00/ProtossMech_Purifier.git) and RimCampaign. Start a new game; 1.6 saves are not supported.

Source code is in `Source/PMP_Persona/PMP_Persona`; build output is written to `1.7/Assemblies`.

Mason and Kaldalis: one shared event budget serves both personalities; pulses spend future opportunities. The honor response uses pawn-local hediff comps, with no storyteller ThinkTree injection.

```powershell
dotnet build Source/PMP_Persona/PMP_Persona/PMP_Persona.csproj --configfile NuGet.Config -v:minimal
```

Runtime distribution consists of `About`, `1.7`, `Languages`, `Textures`, `LoadFolders.xml`, and `Sounds` when present. Keep source code and build intermediates out of the installed mod directory.

## 战役开发交接

`Tools/`、`Docs/`、`artifacts/` 和 `.work/` 仅供本地开发使用，不上传到仓库。当前关卡、世界观、编辑与验收说明保留在本地 `Docs/README.md`。

`Campaigns/MissionOne.project.json.gz` 是战役编辑工程，包含五关及开放关，属于战役剧本，需要保留；`1.7/Campaigns/MissionOne.json.gz` 是游戏加载的战役运行配置，也需要上传。
