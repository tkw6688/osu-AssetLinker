# osu! Lazer → Stable 数据同步工具

把 osu!lazer 的数据同步到 osu!stable，利用**硬链接（hard link）**实现零额外磁盘占用。

> 反方向（stable → lazer）不需要本工具 —— osu!lazer 自带从 stable 导入内容的功能（首次设置向导里的「Import」步骤），同样是硬链接、零额外占用。

## 能同步的内容

| 内容 | 开关 | 说明 |
|------|------|------|
| 铺面 | `--beats` | lazer 的铺面集 → stable 的 `Songs/` |
| 皮肤 | `--skins` | lazer 中已导入的皮肤 → stable 的 `Skins/` |
| 全部 | `--all` | 以上全部 |

不加任何开关时默认只同步铺面。

**暂不支持**：回放（`.osr`）和收藏夹（`collection.db`）。stable 不会自动加载放进 `Replays/` 的回放，只能在 stable 里手动导入；收藏夹需要在 stable 里重建。

## 原理

osu!lazer 把铺面、皮肤等文件以内容哈希（SHA-256）存储在 `files/` 目录下，文件名与原始文件名无关。本工具读取 lazer 的 Realm 数据库，根据哈希找到对应的文件，在 stable 对应的目录下以可读的名字建立硬链接。

**硬链接**使得两个路径指向磁盘上的同一份数据，只有一份数据副本被存储。因此无需复制文件，不占用额外磁盘空间。

铺面的文件夹名沿用 stable 的惯例 `{在线ID} {艺术家} - {标题}`；皮肤沿用 `{名称} ({作者})`。

## 系统要求

- Windows（硬链接 API 为 Windows 专用）
- [.NET 10.0](https://dotnet.microsoft.com/download/dotnet/10.0) 运行时
- osu!lazer 和 osu!stable 的数据目录在**同一磁盘分区**（硬链接要求）

## 安装

从 [Releases](https://github.com/tkw6688/osu-AssetLinker/releases) 下载 `osu-AssetLinker.exe`，放到任意目录运行即可（需要上面的 .NET 10.0 运行时）。

## 用法

### 交互模式（推荐）

直接双击 `osu-AssetLinker.exe`（不传任何参数）进入向导：

1. 依次确认 lazer 数据目录和 stable 的 `Songs` 目录（留空使用自动检测结果）
2. 选择要同步的内容（铺面 / 皮肤，可多选）
3. 选择是否启用 `--relink`
4. 查看操作摘要，确认后才开始执行

### 命令行

```
osu-AssetLinker [lazer目录] [Songs目录] [功能开关] [其他选项]
```

#### 参数

| 参数 | 说明 |
|------|------|
| `lazer目录` | osu!lazer 数据目录（包含 `client.realm`），留空自动检测 `%APPDATA%\osu` |
| `Songs目录` | osu!stable 的 `Songs` 文件夹路径，也可以直接给 stable 根目录。留空自动检测 |

#### 功能开关

| 选项 | 说明 |
|------|------|
| `--beats` | 同步铺面到 `Songs/` |
| `--skins` | 同步皮肤到 `Skins/` |
| `--all` | 同步以上全部 |

#### 其他选项

| 选项 | 说明 |
|------|------|
| `--relink` | 将 stable 中已有的普通文件（之前通过复制产生的）替换为硬链接，释放磁盘空间 |
| `--help`, `-h` | 显示帮助信息 |

### 示例

```sh
# 自动检测路径，同步新铺面到 stable
osu-AssetLinker

# 同步皮肤
osu-AssetLinker --skins

# 同步全部内容，并把已有普通文件替换为硬链接
osu-AssetLinker --all --relink

# 手动指定路径
osu-AssetLinker "C:\Users\你的用户名\AppData\Roaming\osu" "C:\osu!\Songs" --skins

# 手动指定路径 + 重链接
osu-AssetLinker "D:\osu-lazer" "D:\osu-stable\Songs" --all --relink
```

## 注意事项

1. **建议先关闭 osu!lazer** —— 工具以只读方式打开 Realm 数据库，一般不影响 lazer 运行，但如果报错请先关掉 lazer 再试。
2. **硬链接要求同一分区** —— 若 lazer 和 stable 在不同分区，工具会退回到复制模式（占用额外空间），但 `--relink` 模式下会直接报错退出。
3. **同步完成后在 stable 中按 `F5`** 刷新铺面列表；皮肤需要重启 stable 才能在皮肤列表里看到。
4. **不要用 stable 修改这些文件** —— 因为文件是硬链接，在 stable 里编辑铺面或皮肤会直接改动 lazer 中的原始副本。删除 stable 中的文件则是安全的（lazer 那份不受影响）。
5. 如果 lazer 中删除了某个铺面，stable 中已存在的硬链接仍然有效（只要 lazer 的源文件未被删除），但不会自动清理。需要手动删除 stable 中对应的文件夹。
6. Windows 路径最长支持 260 字符（`MAX_PATH`）。本工具会自动检测并截断过长的文件名，确保不超出限制。
7. 本工具只读取 lazer 的数据，**绝不写入**。

## 行为说明

| 场景 | 默认行为 | `--relink` |
|------|----------|------------|
| stable 中不存在该条目 | 创建文件夹，建立硬链接 | 创建文件夹，建立硬链接 |
| stable 中已存在（普通文件，内容相同） | 跳过 | 替换为硬链接，释放空间 |
| stable 中已存在（普通文件，内容不同） | 跳过 | **跳过并警告**，不覆盖 |
| stable 中已存在（硬链接） | 跳过 | 跳过 |

`--relink` 替换前会逐字节校验内容（先比长度，再比 SHA-256）。**内容不同时绝不覆盖** —— 因为 stable 里那份可能是你改过的（例如手动编辑过的 `skin.ini`），覆盖会造成数据丢失。这种情况会在输出里标成 `⚠ N 个文件内容不同，未覆盖`。

**已知限制**：目标文件夹只要存在就不会再检查，**不会补上缺失的文件**（沿用旧版铺面的逻辑）。需要补文件时可以先删掉对应文件夹再同步。

---

## 开发

### 从源码构建

```sh
# 调试构建
dotnet build -c Release

# 发布单文件（框架依赖，发布选项已配置在 csproj 里）
dotnet publish -c Release -o publish
```

发布产物位于 `publish/osu-AssetLinker.exe`（单文件，约 7 MB，不内置运行时）。

仓库里的 `osu/` 是 osu!lazer 源码，仅供对照查阅，已在 `osu-AssetLinker.csproj` 里排除，不参与编译。

### 实现说明

**Realm schema 版本**：lazer 的数据库有 schema 版本号，且**必须精确匹配**才能打开。lazer 每次升级都可能改变这个版本号，因此本工具会先按已知版本尝试，失败时从报错信息中读出真实版本再重试，无需随 lazer 更新而重新编译。

`Realm/LazerModels.cs` 里声明了本工具用到的 lazer 数据模型。如果 lazer 删除或改名了其中某个字段，工具会给出明确报错，此时需要同步更新该文件。

**皮肤文件夹名的匹配**：lazer 从 stable 导入皮肤时会给皮肤改名，在末尾追加 `[原文件夹名]`（见 `SkinImporter.checkSkinIniMetadata`）。所以本工具除了 `{名称} ({作者})` 和 `{名称}`，还会拿这段后缀去匹配 stable 里已有的文件夹，避免同一份皮肤被重复导入。

### 目录结构

```
Program.cs              CLI 解析、调度、汇总输出
Realm/LazerModels.cs    lazer 的 Realm 数据模型（子集声明）
Realm/LazerRealm.cs     以只读方式打开 Realm，自动探测 schema 版本
Sync/SyncContext.cs     同步上下文与统计
Sync/BeatmapSyncer.cs   铺面同步
Sync/SkinSyncer.cs      皮肤同步
Util/HardLink.cs        硬链接原生 API、卷判断
Util/FileSync.cs        单个文件的链接/复制/重链接策略（含内容校验）
Util/LazerFileStore.cs  lazer 文件仓库的哈希路径解析
Util/PathHelper.cs      文件名清洗、MAX_PATH 截断
Util/Ui.cs              控制台输出
```
