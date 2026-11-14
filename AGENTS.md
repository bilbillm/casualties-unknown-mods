# 《未知伤亡》模组开发说明

本仓库包含三个 Unity/BepInEx 5 插件，使用 C# 和 .NET Standard 2.1。

| 目录 | 内容 |
| --- | --- |
| [AstartesMod](AstartesMod/) | 正式版的阿斯塔特可选角色、Mk X 动力甲、武器和专属素材。改动前先读[角色与装备说明](AstartesMod/README.md)。 |
| [HelldiversDropMusic](HelldiversDropMusic/) | 用玩家自备的音频替换开局空降音乐。 |
| [ModListMenu](ModListMenu/) | 在主菜单显示已加载的 BepInEx 插件及其说明。 |

## 构建

`GameDir` 指向游戏安装目录，需要包含 `CasualtiesUnknown_Data/Managed/` 和 `BepInEx/core/`。阿斯塔特当前验证基准为 Steam 正式版 Build 26590676。

```powershell
$gameDir = 'D:\SteamLibrary\steamapps\common\Casualties Unknown'
dotnet build .\AstartesMod\AstartesMod.csproj -c Release "-p:GameDir=$gameDir"
```

阿斯塔特输出到 `AstartesMod/bin/Release/`。安装时把 DLL、`modinfo.json` 和整个 `Sprites/` 复制到游戏的 `BepInEx/plugins/AstartesMod/`。两个辅助插件的项目文件默认使用 Demo 安装路径。为正式版构建时指定 `-p:GameDir`，只编译时同时用 `-p:OutputPath` 指定一个本地输出目录。

## 阿斯塔特代码与素材

- `Plugin.cs` 初始化配置、资源和 Harmony 补丁。`Character/AstartesMenu.cs` 和 `Character/AstartesRun.cs` 处理选角、开局和存档。角色 ID 是 `astartes`，存档身份字段为 `casualtiesunknown.astartes-mod`。
- `Character/AstartesBody.cs`、`AstartesPhysiology.cs` 和 `AstartesOrgans.cs` 处理角色本体。动力甲加成按实际穿戴结算，由装备和灯光模块处理。改动开局或装备时，核对新局、读档和换层。
- `Sprites/` 放游戏加载的贴图。`Art/` 保留可编辑源图和生成提示词。`Dialogue/` 的中英文台词和 `Audio/` 的 WAV 在构建时嵌入 DLL。音频来源记在 [Audio/SOURCES.md](AstartesMod/Audio/SOURCES.md)。

## 提交约定

- 注释、文档和提交信息以中文为主。
- 提交源码、运行所需素材和对应文档。构建产物、运行日志及本地输出文件由 `.gitignore` 排除。
- 运行问题可查游戏目录中的 `BepInEx/LogOutput.log`。
