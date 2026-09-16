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

默认是**框架依赖**发布，压缩包很小（约 300 KB），使用者需要装 .NET 10 桌面运行时。

如果需要免安装运行时的版本，把 workflow 里 `--self-contained false` 改成 `--self-contained true`，代价是压缩包会涨到 100 MB 以上。
