# osu! Lazer → Stable 数据同步工具

把 osu!lazer 的铺面和皮肤同步到 osu!stable，利用**硬链接（hard link）**实现零额外磁盘占用。

> 反方向（stable → lazer）不需要本工具：osu!lazer 自带从 stable 导入内容的功能（首次设置向导里的「Import」步骤），同样是硬链接、零额外占用。

## 能同步的内容

| 内容 | 开关 | 说明 |
|------|------|------|
| 铺面 | `--beats`（默认） | lazer 的铺面集 → stable 的 Songs 目录 |
| 皮肤 | `--skins` | lazer 中已导入的皮肤 → stable 的 `Skins/` |
| 全部 | `--all` | 铺面 + 皮肤 |

**暂不支持**：回放（`.osr`）和收藏夹（`collection.db`）。stable 不会自动加载放进 `Replays/` 的回放；收藏夹需要在 stable 里重建。

## 原理

osu!lazer 把铺面、皮肤等文件以内容哈希（SHA-256）存储在 `files/` 目录下，文件名与原始文件名无关。本工具读取 lazer 的 Realm 数据库，根据哈希找到对应文件，在 stable 目录下以可读的名字建立硬链接。

硬链接让两个路径指向磁盘上的同一份数据，因此无需复制文件，不占用额外磁盘空间。铺面文件夹名沿用 stable 的惯例 `{在线ID} {艺术家} - {标题}`，皮肤沿用 `{名称} ({作者})`。

## 系统要求与安装

- Windows（硬链接 API 为 Windows 专用）
- [.NET 10.0](https://dotnet.microsoft.com/download/dotnet/10.0) 运行时
- osu!lazer 和 osu!stable 的数据目录在**同一磁盘分区**（硬链接要求；不同分区会退回复制）

从 [Releases](https://github.com/tkw6688/osu-AssetLinker/releases) 下载 `osu-AssetLinker.exe`，放到任意目录运行即可。

## 快速开始

### 交互模式（推荐）

直接双击 `osu-AssetLinker.exe`（不传参数）进入向导：

1. 确认 lazer 数据目录和 osu!stable 根目录（留空使用自动检测）
2. 选择要同步的内容（铺面 / 皮肤）
3. 选择是否启用 `--relink`
4. 选择是否在执行前先预览（选「是」会先只读扫描，确认后再执行）
5. 查看摘要并确认

### 命令行

```sh
# 先预览，不写入任何文件
osu-AssetLinker --all --dry-run

# 确认后正式同步
osu-AssetLinker --all
```

## 命令行用法

```
osu-AssetLinker [--lazer-dir <目录>] [--stable-dir <目录>] [功能开关] [其他选项]
```

功能开关：`--beats`（默认）、`--skins`、`--all`，含义见上方「[能同步的内容](#能同步的内容)」。

### 路径参数

| 参数 | 说明 |
|------|------|
| `--lazer-dir` | lazer 数据目录（含 `client.realm`）。省略时自动检测 `%APPDATA%\osu`；若 lazer 使用了自定义数据目录，会读取 `storage.ini` 自动跟随 |
| `--stable-dir` | stable 根目录（含 `osu!.exe` / `Songs` / `Skins` / `osu!*.cfg`）。省略时自动检测；也接受 `Songs` 或 `Skins` 目录，会自动改用其父目录 |

铺面目标优先使用 stable 配置 `osu!.{用户名}.cfg` 里的 `BeatmapDirectory`（相对 stable 根目录解析），没有可用配置时使用 `<stable-dir>\Songs`；皮肤固定写入 `<stable-dir>\Skins`。

> 旧的位置参数 `[lazer目录] [Songs目录]` 已移除，请改用 `--lazer-dir` / `--stable-dir`。

### 其他选项

| 选项 | 说明 |
|------|------|
| `--relink` | 把 stable 中已有的普通文件替换为硬链接，释放重复占用的空间 |
| `--dry-run` | 只预览，不创建、修改或删除任何文件 |
| `--version`, `-v` | 显示版本与构建提交 |
| `--help`, `-h` | 显示帮助信息 |

### 示例

```sh
osu-AssetLinker
osu-AssetLinker --skins
osu-AssetLinker --all --relink
osu-AssetLinker --lazer-dir "D:\osu-lazer" --stable-dir "D:\osu-stable" --all --relink
```

## 注意事项

1. **建议先关闭 osu!lazer**：工具以只读方式打开 Realm，一般不影响运行，但如果报错请先关掉 lazer。
2. **硬链接要求同一分区**：不同分区会自动退回复制模式；`--relink` 模式下会直接报错退出。
3. **同步完成后在 stable 中按 `F5`** 刷新铺面；皮肤需要重启 stable 才能看到。
4. **不要用 stable 修改这些文件**：硬链接意味着在 stable 里编辑铺面或皮肤会直接改动 lazer 中的原始副本。删除 stable 中的文件是安全的。
5. lazer 中删除铺面后，stable 里已有的硬链接不会自动清理，需要手动删除对应文件夹。
6. Windows 路径上限 260 字符（`MAX_PATH`），工具会自动截断过长的文件名。
7. 工具**只读取** lazer 数据，绝不写入。
8. lazer 使用自定义数据目录时，`%APPDATA%\osu` 可能只剩 `storage.ini` 等残留文件；工具会自动跟随 `storage.ini` 的 `FullPath`，也可以用 `--lazer-dir` 直接指定。

## 行为说明

| 场景 | 默认行为 | `--relink` |
|------|----------|------------|
| stable 中不存在该条目 | 创建文件夹，建立硬链接 | 创建文件夹，建立硬链接 |
| stable 中已存在（普通文件，内容相同） | 跳过 | 替换为硬链接，释放空间 |
| stable 中已存在（普通文件，内容不同） | 跳过 | **跳过并警告**，不覆盖 |
| stable 中已存在（硬链接） | 跳过 | 跳过 |

`--relink` 替换前会先比长度、再比 SHA-256。**内容不同时绝不覆盖**，因为 stable 里那份可能是你改过的（例如手动编辑过的 `skin.ini`）。输出中会标为 `⚠ N 个文件内容不同，未覆盖`。

**已知限制**：目标文件夹只要存在就不会再检查，不会补上缺失的文件。需要补文件时可以删除对应文件夹后重新同步。

## 退出码

| 退出码 | 含义 |
|--------|------|
| `0` | 成功（或用户取消） |
| `1` | 参数、路径或数据库错误 |
| `2` | 同步完成，但有文件或条目失败 |

## 开发

构建、测试、发布流程和实现说明见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 许可证

[MIT](LICENSE)
