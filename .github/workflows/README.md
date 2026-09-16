# 工作流说明

`build.yml` 一个文件覆盖三种触发方式：

| 触发 | 行为 |
|---|---|
| 推送到 `main` | 编译 → 打包 → 更新名为 **latest** 的预发布 Release（覆盖式，只保留最新构建） |
| 推送 `v*` 标签 | 编译 → 打包 → 创建正式 Release，自动生成更新日志 |
| Pull Request | 只编译验证，不产出 Release |
| 手动触发 | 在 Actions 页面点 "Run workflow" 即可重新构建 |

## 发正式版本

```bash
git tag v1.0.0
git push origin v1.0.0
```

几秒钟后 Actions 会跑起来，完成后在仓库 Releases 页面出现 `v1.0.0`，附件是 `DingPanMao-v1.0.0-win-x64.zip`。

## 产物说明

每个 Release 都会同时提供两个版本，用户自己挑：

| 文件 | 体积 | 适用 |
|---|---|---|
| `DingPanMao-<版本>-win-x64.zip` | 约 400 KB | 机器上已装 .NET 10 桌面运行时 |
| `DingPanMao-<版本>-win-x64-standalone.zip` | 约 60–80 MB | 免安装运行时，解压双击即用 |

两个版本由同一个矩阵任务并行构建（`matrix.self_contained` 控制），构建产物先上传成 artifact，再由 `release` 任务统一下载并一次性发布，避免两个任务并发写同一个 Release。
