# osu! Lazer → Stable 铺面同步工具

将 osu!lazer 的铺面导入到 osu!stable 中，利用**硬链接（hard link）**实现零额外磁盘占用。

## 原理

osu!lazer 将铺面文件以内容哈希（SHA-256）存储在 `files/` 目录下，文件名与原始文件名无关。本工具读取 lazer 的 Realm 数据库，根据哈希找到对应的文件，在 stable 的 `Songs/` 目录下以可读的文件夹名建立硬链接。

**硬链接**使得两个路径指向磁盘上的同一份数据，只有一份数据副本被存储。因此无需复制文件，不占用额外磁盘空间。

## 系统要求

- Windows（硬链接 API 为 Windows 专用）
- [.NET 8.0](https://dotnet.microsoft.com/download/dotnet/8.0) 运行时
- osu!lazer 和 osu!stable 的 `Songs` 目录在**同一磁盘分区**（硬链接要求）

## 安装

### 下载预编译版本

从 [Releases](https://github.com/anomalyco/osu-lazer-to-stable/releases) 下载 `osu-lazer-to-stable.exe`。

### 自行编译

```sh
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true /p:PublishReadyToRun=false -o publish
```

输出文件位于 `publish/osu-lazer-to-stable.exe`。

## 用法

```
osu-lazer-to-stable [lazer目录] [Songs目录] [选项]
```

### 参数

| 参数 | 说明 |
|------|------|
| `lazer目录` | osu!lazer 数据目录（包含 `client.realm`），留空自动检测 `%APPDATA%\osu` |
| `Songs目录` | osu!stable 的 `Songs` 文件夹路径，留空自动检测 |

### 选项

| 选项 | 说明 |
|------|------|
| `--relink` | 将 stable 中已有的普通文件（之前通过复制产生的）替换为硬链接，释放磁盘空间 |
| `--help`, `-h` | 显示帮助信息 |

### 示例

```sh
# 自动检测路径，同步新铺面到 stable
osu-lazer-to-stable

# 自动检测，并将已有普通文件替换为硬链接
osu-lazer-to-stable --relink

# 手动指定路径
osu-lazer-to-stable "C:\Users\你的用户名\AppData\Roaming\osu" "C:\osu!\Songs"

# 手动指定路径 + 重链接
osu-lazer-to-stable "D:\osu-lazer" "D:\osu-stable\Songs" --relink
```

## 注意事项

1. **运行前请关闭 osu!lazer** — Realm 数据库需要独占访问。
2. **硬链接要求同一分区** — 若 lazer 和 stable 在不同分区，工具会退回到复制模式（占用额外空间），但 `--relink` 模式下会直接报错退出。
3. **同步完成后在 stable 中按 `F5`** 刷新铺面列表。
4. 如果 lazer 中删除了某个铺面，stable 中已存在的硬链接仍然有效（只要 lazer 的源文件未被删除），但不会自动清理。需要手动删除 stable 中对应的文件夹。
5. Windows 路径最长支持 260 字符（`MAX_PATH`）。本工具会自动检测并截断过长的文件名，确保不超出限制。

## 行为说明

| 场景 | 默认行为 | `--relink` |
|------|----------|------------|
| stable 中不存在该铺面 | 创建文件夹，建立硬链接 | 创建文件夹，建立硬链接 |
| stable 中已存在（普通文件） | 跳过 | 替换为硬链接，释放空间 |
| stable 中已存在（硬链接） | 跳过 | 跳过 |
