# MobGrinder

基于 Dalamud API 15 的《最终幻想 XIV》自动刷野怪插件，按预设传送到地图、前往静态刷新点并扫描目标。战斗技能由其他战斗插件负责。

当前项目版本为 `0.1.0.0`。源码仓库：<https://github.com/irimsky/MobGrinder>。

首个公开版本 [`v0.1.0.0`](https://github.com/irimsky/MobGrinder/releases/tag/v0.1.0.0) 已发布。

## 功能和依赖

MobGrinder 使用以下服务完成自动流程：

- Dalamud API 15；
- vnavmesh：飞行、跑图和导航；
- Lifestream：传送和区域移动；
- 其他战斗插件：负责实际战斗技能；
- `FieldNavigation`：构建时引入的公开共享导航源码，位于 [`MobGrinder/external/FieldNavigation/`](./MobGrinder/external/FieldNavigation/)。

野怪身份使用 `IBattleNpc.NameId`，不使用 `StatusFlags.Hostile`。FATE、战斗标签和友方判定经过统一候选过滤。

## 安装

可以从 [GitHub Releases](https://github.com/irimsky/MobGrinder/releases) 下载当前版本的 [`latest.zip`](https://github.com/irimsky/MobGrinder/releases/download/v0.1.0.0/latest.zip)，并按 Dalamud 的本地插件方式安装。

第三方 Dalamud 插件清单仓库为 [`irimsky/DalamudPlugins`](https://github.com/irimsky/DalamudPlugins)，订阅地址为 [`manifest.json`](https://raw.githubusercontent.com/irimsky/DalamudPlugins/main/manifest.json)。Release workflow 默认使用这个仓库；只有需要覆盖默认目标时才配置 `MOBGRINDER_MANIFEST_REPOSITORY`。若清单条目已经合并，也可以在 Dalamud 的第三方插件仓库设置中添加该 Raw 地址。

首次使用前请确认 vnavmesh、Lifestream 和战斗插件已经安装并正常运行。缺少这些依赖时，MobGrinder 会停止相关流程，不会替代战斗插件执行技能。

## 使用

在游戏内使用以下命令：

```text
/mobgrinder 或 /mg       打开设置窗口
/mg start                开始自动流程
/mg pause                暂停流程并停止当前导航
/mg stop                 停止流程并清空扫描结果
/mg scan                 立即执行一次扫描
/mg status               输出当前状态
/mg log                  打开诊断日志
```

开始自动流程前，请在设置窗口中配置目标野怪、地图、刷新点、运行模式和停止条件。

## 本地构建

构建环境要求：

- Windows；
- .NET SDK `10.0.400` 或 `global.json` 允许的同一 feature band 版本；
- 包含 `Dalamud.dll` 的 Dalamud API 15 runtime；
- PowerShell 7（`pwsh`）。

如果构建脚本无法自动找到本机 Dalamud runtime，可以显式指定路径：

```powershell
pwsh -File .\build.ps1 -Configuration Debug -Restore -DalamudLibPath "D:\path\to\Dalamud\runtime"
```

也可以设置以下任一环境变量：

```powershell
$env:DALAMUD_LIB_PATH = "D:\path\to\Dalamud\runtime"
```

支持的路径优先级为：命令行 `-DalamudLibPath`、`MOBGRINDER_DALAMUD_LIB_PATH`、`DALAMUD_LIB_PATH`、`DALAMUD_HOME`，然后是本机 XIVLauncher 的开发 runtime 目录。

在仓库根目录执行构建和测试：

```powershell
# Debug 构建
pwsh -File .\build.ps1 -Configuration Debug -Restore

# 锁定依赖的 Release 构建
pwsh -File .\build.ps1 -Configuration Release -Restore -LockedMode

# 测试项目还原和测试
dotnet restore .\MobGrinder.Tests\MobGrinder.Tests.csproj -p:DalamudLibPath="$env:DALAMUD_LIB_PATH"
dotnet test .\MobGrinder.Tests\MobGrinder.Tests.csproj -c Release --no-restore -p:DalamudLibPath="$env:DALAMUD_LIB_PATH"

# 检查插件 ZIP
pwsh -File .\tools\Test-PluginPackage.ps1 -PackagePath .\MobGrinder\bin\Release\MobGrinder\latest.zip
```

Release ZIP 位于 `MobGrinder/bin/Release/MobGrinder/latest.zip`，DLL 和 JSON 位于 ZIP 根目录。构建不会把 Dalamud runtime、测试结果、日志或本地配置打进插件包。

## 发布

本地发布前检查：

```powershell
pwsh -File .\release.ps1 0.1.0.0 -Restore -RequireMainBranch -RequireCleanWorkspace
```

`release.ps1` 只执行版本检查、Release 构建、ZIP 检查和发布元数据生成，不创建 tag、不推送、不发布 GitHub Release，也不修改外部 manifest。

GitHub Actions 的行为如下：

- `Pull Request Build`：PR 自动运行，也可以在 Actions 页面手动运行；
- `Release`：推送匹配版本的 tag 后构建并发布 GitHub Release；
- 外部 Dalamud manifest 更新：只有手动运行 Release workflow、选择 `manifest_update=pr`，并配置 `DALAMUD_MANIFEST_TOKEN` 后才会创建 PR；目标仓库默认是 `irimsky/DalamudPlugins`，仓库变量仅用于覆盖默认值。

当前状态：GitHub Release `v0.1.0.0` 已创建，Release workflow 已完成构建和发布链路；插件仍尚未完成游戏内加载、重载、卸载、登出、切地图、依赖失效和完整战斗流程验证。
