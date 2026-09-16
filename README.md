# 盯盘猫 · 任务栏行情条

一个常驻 Windows 任务栏的小工具，实时显示你关心的任何行情：**A股、港股、美股、指数、黄金、原油、外汇、美债收益率**，并在关键位置触发提醒。

## 功能

- **任务栏常驻**：无边框长条贴在任务栏空白处，不占用任务栏按钮，不抢焦点，切换任何应用都不会被盖住
- **任意品种**：内置 30 个常用品种，也可以在设置里按名称或代码搜索任意品种（A股 / 港股 / 美股 / 指数 / 商品 / 债券 / 外汇）
- **最多 4 个格子**，每个格子可选三种尺寸
  - 小：只显示名称和涨跌百分比
  - 中：名称、涨跌百分比、走势图
  - 大：名称、现价、涨跌百分比、走势图
- **实时走势图**：默认显示最近 60 分钟，10 秒刷新
- **三类提醒**（阈值按各品种的 ATR 自适应）
  - 触底反弹 / 摸顶回落
  - 突破日内新高 / 新低
  - 上破压力位 / 跌破支撑位（枢轴点 + 近期摆动点 + 整数关口）
- **安静的提醒方式**：触发时对应格子闪两下并把文字换成提醒内容，4 秒后恢复，不弹窗、不出声
- **拖动记忆**：长条可以左右拖动，松手记住位置，下次启动回到原处
- **多语言**：简体中文 / 繁体中文 / English / 日本語 / 한국어 / Русский / Español / Français / Deutsch / Português
- **可调外观**：不透明度、字号、红涨绿跌或绿涨红跌
- **点击看大图**：点格子弹出当日分时和近 30 日走势（可在设置里关闭）
- **滚轮换品种**：在格子上滚动鼠标滚轮，在内置品种间快速切换
- **数据源降级**：东方财富优先，失败时自动切到新浪财经，全挂时格子变灰提示

## 快速开始

1. 下载 `dist/DingPanMao.exe`
2. 双击运行，长条会出现在任务栏系统托盘的左边
3. 右键长条 → **设置…** 挑选品种、语言和提醒开关

需要 .NET 10 桌面运行时。若想免安装运行时，用下面的自包含发布方式重新构建。

## 使用说明

| 操作 | 效果 |
|---|---|
| 左键按住拖动 | 左右移动长条，松手记住位置 |
| 鼠标滚轮 | 在该格子的内置品种间循环切换 |
| 左键单击 | 打开该品种的大图（可在设置里关闭） |
| 右键 | 最近提醒记录 / 设置 / 复位位置 / 退出 |

设置保存在 `%LOCALAPPDATA%\DingPanMao\settings.json`，运行异常会记录在 `%LOCALAPPDATA%\DingPanMao\error.log`。

## 数据源

行情来自公开的免费接口，**无需注册、无需 API Key**：

| 数据源 | 覆盖范围 | 用途 |
|---|---|---|
| 东方财富 | A股、港股、美股、指数、商品、债券、外汇 | 主数据源，同时提供日K与分时 |
| 新浪财经 | A股、港股、美股、外盘期货与贵金属 | 东方财富失败时降级使用 |

报价为快照行情，有数秒到数十秒的延迟，**仅供个人参考，不适合作为交易依据**。

## 多语言

界面文案全部在 [`src/DingPanMao/Resources/Strings.json`](src/DingPanMao/Resources/Strings.json)，一种语言一个对象。想加新语言，复制一份改掉 key 里的文字，再把语言代码加进 `Lang` 的可选列表即可。

## 构建

```bash
# 开发调试
dotnet build src/DingPanMao/DingPanMao.csproj

# 单文件发布（依赖已安装的 .NET 运行时，约 200 KB）
dotnet publish src/DingPanMao/DingPanMao.csproj -c Release -r win-x64 \
  --self-contained false -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o dist

# 自包含发布（体积大，但目标机器不需要装运行时）
dotnet publish src/DingPanMao/DingPanMao.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

## 项目结构

```
src/DingPanMao/
├── Config/          # 配置模型与读写
├── Controls/        # 格子控件、走势图
├── Interop/         # 任务栏定位与窗口样式（Win32）
├── Models/          # 品种、行情、关键位、提醒等模型
├── Resources/       # 多语言包
├── Services/        # 数据源、指标、提醒引擎、本地化
└── Views/           # 设置、品种搜索、大图窗口
```

## 实现要点

- 长条是一个无边框 + 置顶 + 不抢焦点的窗口（`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`），定位在系统托盘左侧
- 任务栏的托盘区是 `Shell_TrayWnd` 的子窗口，必须用 `FindWindowEx` 才能拿到它的位置
- 监听 `EVENT_SYSTEM_FOREGROUND`，任何应用切到前台时立刻重新置顶
- 提醒阈值 = 该品种 14 日 ATR × 系数，行情平淡和剧烈时都能保持合理灵敏度
- 报价时间落后本地时间超过 5 分钟视为休市，暂停提醒，避免收盘后误报

## 图标

图标源文件放在仓库根目录的 `icon.png`，用 `tools/make_icon.py` 生成多尺寸 `.ico`：

```bash
python tools/make_icon.py
```

脚本会补上圆角透明遮罩（源图四角是纯黑，没有 alpha），并按 16/20/24/32/48/64/128/256 每个尺寸单独缩放锐化，输出到 `src/DingPanMao/Resources/app.ico`。

## License

[MIT](LICENSE)

---

## English

**盯盘猫 (DingPanMao)** is a tiny Windows taskbar companion that shows live quotes for A-shares, HK and US stocks, indices, gold, crude oil, FX and US Treasury yields — and quietly alerts you when price reverses at a session extreme or crosses a support/resistance level.

Built with WPF on .NET 10. Data comes from public EastMoney and Sina endpoints (EastMoney first, Sina as fallback). Up to 4 slots, three sizes each, ten UI languages, adjustable opacity and colors. Drag it along the taskbar and it remembers where you left it.

See the Chinese section above for full details. Licensed under MIT.
