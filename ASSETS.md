# 角色素材与替换接口

本源码版本不包含旧演示版使用的人物图片、图标或 DSH 转换动画。
原素材的非商业限制不会因为程序代码采用 MIT 而消失，因此不能从旧版程序中提取后作为“可商用素材”重新发布。

没有角色素材时，构建脚本用 System.Drawing 绘制蓝色圆形和白色方块等简单几何占位图。
占位图生成代码与项目原有代码一同按 MIT 许可发布。它们仅用于开发验证，不代表正式角色设计。

## 静态素材

使用你原创、取得商业再分发授权或许可明确允许商业再分发的素材。
将下列文件放入项目的 `assets/`，或通过构建参数指定其他目录：

| 文件 | 作用 |
| --- | --- |
| `pet.png` | 默认正面图，透明 PNG |
| `pet.ico` | 程序及托盘图标 |
| `pet-side-alpha.png` | 侧面移动图，透明 PNG |
| `pet-back-alpha.png` | 背面图，透明 PNG |
| `pose-sleep.png` | 休息图，透明 PNG |
| `pose-flower.png` | 摸摸反馈图，透明 PNG |
| `pose-rice.png` | 喂食反馈图，透明 PNG |

为便于对齐，各张 PNG 建议采用相同画布与一致的人物占比。

```powershell
.\source\build.ps1 -AssetDirectory '.\assets'
```

显式指定素材目录时，缺少必需素材会报错，不会悄悄混用占位图。
默认未指定目录且没有 `pet.png` 时，则整套使用几何占位图。

## 可选动作动画

可放入 `assets/animations/`：

`idle-breath.gif`、`stretch.gif`、`yawn.gif`、`nap.gif`、`think.gif`、`daydream.gif`、`note.gif`、`flowers.gif`、`bubbles.gif`、`celebrate.gif`。

这些 GIF 均为可选输入，缺少时不影响静态角色与其他桌宠功能的编译。
现有动画播放逻辑以约 10 fps 的源动画为基准。原有 DSH 素材本身未随源码提供；代码里的兼容命名不构成对原素材的授权。

新增素材时请在仓库中保存其作者、来源、许可文本及必要的修改说明。
若正式素材的许可不同于 MIT，应在本文件和 THIRD_PARTY_NOTICES.md 中准确写明；不要仅凭文件位于 MIT 仓库中就认定其也采用 MIT。

## 皮肤包与共享动作

公开版默认生成三套原创几何占位皮肤；不包含楠小秾、GPT 白龙娘、Claude 大小姐等演示角色素材。
若有对应角色的合法素材，可创建任意 id / name 的皮肤包，再从“个性化 → 更换皮肤 → 打开皮肤文件夹”导入。

运行目录中的 `assets/skins/<id>/skin.json` 指定 `schemaVersion: 1`、`id`、`name`、可选 `sideFacesLeft` / `mouth`，以及 `poses`。

14 个必需姿态为：front、side、back、sleep、flower、rice、blink、stretch、yawn、think、daydream、note、bubbles、celebrate。
每项包含 `image`（包内透明 PNG）、`rect`（图集矩形 x/y/宽/高）、`height`（70 至 260 的显示高度）；角色不得贴到矩形边缘。
`name` 最多 32 字，`id` 使用小写字母、数字、下划线或连字符。侧面原图朝左时设 `sideFacesLeft: true`。

完整可生成示例见 `source/make-placeholder-skins.ps1`。更换或刷新皮肤时，动作时间轴和人物图像分别管理，始终使用所选皮肤的姿态。
默认皮肤可放在构建素材目录的 `skins/default/` 内并随程序内嵌；缺少该定义时仍支持原来的七项静态素材接口和可选 GIF。
动画统一以 1.1 倍速播放，不改变两项互动之间的 2 秒间隔。
