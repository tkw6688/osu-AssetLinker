# 开发与贡献

## 环境

- Windows
- .NET 10 SDK

## 构建与测试

```sh
dotnet build osu-AssetLinker.slnx -c Release
dotnet test osu-AssetLinker.slnx -c Release
```

测试只使用系统临时目录，不需要安装 osu!lazer 或 osu!stable。

## 发布

```sh
dotnet publish src/osu-AssetLinker/osu-AssetLinker.csproj -c Release -o publish
```

产物是 `publish/osu-AssetLinker.exe` 单文件（框架依赖，约 5 MB，不内置运行时）。

推送日期 tag（如 `20261118-v2`）或 `v*` tag（如 `v1.1.0`）会触发 `.github/workflows/release.yml`：构建、测试、发布并创建 GitHub Release。版本号取自 tag（去掉开头的 `v`），通过 `InformationalVersion` 注入。

仓库里的 `osu/` 是 osu!lazer 源码，仅供对照查阅，位于项目目录之外，不参与编译。

## 目录结构

主项目位于 `src/osu-AssetLinker/`，测试项目位于 `tests/osu-AssetLinker.Tests/`。

```
Program.cs                   入口：编码、Banner、--help
Cli/                         参数解析、帮助、交互向导、控制台会话
App/                         主流程、路径解析、结果汇总
Lazer/LazerModels.cs         lazer 的 Realm 数据模型（子集声明）
Lazer/LazerRealm.cs          以只读方式打开 Realm，自动探测 schema 版本
Lazer/LazerData.cs           lazer 数据目录 / storage.ini / Realm 文件定位
Lazer/LazerFileStore.cs      lazer 文件仓库的哈希路径解析
Stable/StableData.cs         stable 根目录、cfg（BeatmapDirectory）与 Songs 解析
Stable/StableModels.cs       stable 路径与解析结果模型
Sync/BeatmapSyncer.cs        铺面同步
Sync/SkinSyncer.cs           皮肤同步
Sync/SyncContext.cs          同步上下文
Sync/SyncStats.cs            同步统计
Sync/FileJobExecutor.cs      文件任务执行（链接/复制/relink 与计数）
Sync/FileJob.cs              文件任务与执行模式
Sync/DestPathResolver.cs     目标路径分配（长路径冲突避让）
Sync/BeatmapFolderNaming.cs  铺面文件夹命名
Linking/HardLink.cs          硬链接原生 API、卷判断
Linking/FileSync.cs          单个文件的链接/复制/重链接策略（含内容校验）
Linking/PathSanitizer.cs     文件名/路径分量清洗
Linking/PathLength.cs        MAX_PATH 截断
ConsoleUi/Ui.cs              控制台输出
```

## 实现说明

**Realm schema 版本**：lazer 的数据库有 schema 版本号，且必须精确匹配才能打开。lazer 每次升级都可能改变这个版本号，因此工具会先按已知版本尝试，失败时从报错信息中读出真实版本再重试，无需随 lazer 更新而重新编译。

`src/osu-AssetLinker/Lazer/LazerModels.cs` 里声明了本工具用到的 lazer 数据模型。如果 lazer 删除或改名了其中某个字段，工具会给出明确报错，此时需要同步更新该文件。

**lazer 自定义数据目录**：自定义路径记录在默认目录的 `storage.ini`（键 `FullPath`）中。此时 `client.realm`、`files/` 都在自定义目录下，默认目录只留下 `storage.ini`、`client.realm.lock` 等迁移时被跳过的文件。工具会自动跟随，也可以用 `--lazer-dir` 直接指定。

**stable Songs 目录**：stable 用户配置是根目录下的 `osu!.{用户名}.cfg`，其中的 `BeatmapDirectory` 才是实际使用的铺面目录；`osu!.cfg` 是更新清单，不含该键。工具优先读前者，解析相对路径时以 stable 根目录为基准。

**皮肤文件夹名的匹配**：lazer 从 stable 导入皮肤时会给皮肤改名，在末尾追加 `[原文件夹名]`（见 `SkinImporter.checkSkinIniMetadata`）。所以本工具除了 `{名称} ({作者})` 和 `{名称}`，还会拿这段后缀去匹配 stable 里已有的文件夹，避免同一份皮肤被重复导入。

**relink 的内容校验**：替换前先比较文件长度，再比较 SHA-256。内容不同时不覆盖，因为 stable 里那份可能是用户手动修改过的（例如 `skin.ini`）。替换采用「先建临时链接，再覆盖目标」的方式，失败时不会留下空洞。

**长路径**：Windows `MAX_PATH` 为 260 字符。目标路径超长时会截断文件名并保留扩展名；同一批次内的路径分配只依赖已分配集合，保证新建和 relink 使用一致的映射。

## 测试

- `tests/osu-AssetLinker.Tests` 使用 xUnit。
- 覆盖参数解析、路径清洗与截断、目标路径映射、stable 配置解析、lazer `storage.ini` 与 Realm 文件定位、文件任务执行与 relink 策略。
- 测试不读写真实的 osu!lazer / osu!stable 数据；涉及 Realm 数据库和真实目录的集成行为需要手动验证。
