# 构建与开发

这是 Windows 桌宠的源码版本。原角色图片、动画、用户数据、历史程序和 Harness 凭据均未包含。
默认构建自动生成简单几何占位图，正式角色素材的接入方式见 [ASSETS.md](ASSETS.md)。

## Windows 桌宠

需要 64 位 Windows 和 .NET Framework（建议 4.8）。待办窗口需要已安装 Microsoft Edge WebView2 Runtime；系统语音功能取决于 Windows 已安装的语音组件和语言。

在项目根目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\source\build.ps1
```

首次构建从 NuGet 下载锁定版本的 `Microsoft.Web.WebView2 1.0.4129.50`，并校验 SHA-256。依赖保存在 `source/packages/`，不会提交到仓库。
离线环境可先运行下面的命令，提供同版本官方 `.nupkg` 文件：

```powershell
.\source\restore-dependencies.ps1 -PackagePath 'C:\Downloads\Microsoft.Web.WebView2.1.0.4129.50.nupkg'
.\source\build.ps1
```

完整运行目录为 `artifacts/bin/`，主程序为 `LittleDeepSeek.exe`。请保留同目录下的 DLL、`todo-app/` 和许可说明。
双击主程序启动，右键菜单或系统托盘菜单退出。程序不会自动设置开机启动。
任务、提醒记录和设置保存在运行目录的 `data/` 中；目录不可写时，设置会回退到当前用户的本地应用数据目录。

指定输出位置：

```powershell
.\source\build.ps1 -OutputPath '.\artifacts\custom\LittleDeepSeek.exe'
```

## 待办界面源码

仓库保留当前可运行的静态界面 `todo-app/`，普通桌宠构建不要求 Node.js。
修改界面时，需要 Node.js 22.13+，然后运行：

```powershell
cd .\todo-web
npm ci
npm run typecheck
npm run build
cd ..
.\source\build.ps1
```

界面源码位于 `todo-web/`；Vite 构建只更新本项目的 `todo-app/`，不会修改原开发目录。

## 验证

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\source\tests\run-todo-reminder-tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\source\tests\run-status-reporter-tests.ps1
```

待办测试检查解析、提醒轮换、完成去重和记录恢复；状态测试检查天气解析、硬件提醒阈值，并读取一次本机硬件状态。
默认不执行在线天气查询。
鼠标交互、透明窗口、删除文件动画、麦克风、系统语音和多屏显示仍需要在 Windows 桌面上人工体验。

## 可选的 Harness 接入

桌宠本身不附带 AI 服务账号或密钥。`deepseek求索` 功能调用用户另外安装并配置的 DeepSeek Harness。
可通过环境变量指定：

| 变量 | 用途 |
| --- | --- |
| `LITTLEDEEPSEEK_NODE` | `node.exe` 的完整路径；不设置时查找常见安装目录和 PATH |
| `LITTLEDEEPSEEK_HARNESS_ENTRY` | Harness 的 `lib/bin.js` 完整路径 |
| `LITTLEDEEPSEEK_WORKSPACE` | Harness 工作目录；不设置时使用当前用户主目录 |
| `DSH_HOME` | Harness 数据目录；已设置的值会被保留 |
| `NPM_CONFIG_CACHE` | 可选 npm 缓存目录，用于查找 npx 安装的 Harness |

未显式指定入口时，程序尝试当前用户的 npm 全局安装目录和 npx 缓存。
Harness 的任务执行、模型、工具权限及费用由用户自己的 Harness 配置决定。
天气功能仅在手动查询时访问 `wttr.in`；待办界面的普通增删改查在本地完成。
