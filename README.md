# 小deepseek

## 下载使用

**[点击前往 Windows 下载页](https://github.com/nayoagan-ship-it/LittleDeepSeek/releases/latest)** · **[直接下载 Windows 64 位压缩包](https://github.com/nayoagan-ship-it/LittleDeepSeek/releases/latest/download/LittleDeepSeek-Windows-x64-placeholder.zip)**

下载后完整解压，双击文件夹中的 `LittleDeepSeek.exe` 即可启动，无需自己编译。退出时使用桌宠右键菜单或系统托盘菜单。

这是 Windows 桌面程序，需要下载后运行，不能在浏览器中直接使用。需要 64 位 Windows 和 .NET Framework 4.8；待办窗口另需 Microsoft Edge WebView2 Runtime。

当前公开下载版使用可换肤的几何占位角色，不包含原角色图片和动画。请保留压缩包中的 DLL、`assets`、`todo-app` 和许可文件。校园地图内置公共服务，免登录、无需填写 API 密钥；可选的本地任务执行功能使用另行安装的 Harness。

源码构建与详细配置见 [BUILDING.md](BUILDING.md)，素材说明见 [ASSETS.md](ASSETS.md)。

## v0.2.0 更新

- 支持皮肤包，切换形象时同步切换角色名字、托盘及工具窗口标题。
- 13 项可爱互动直接展开，按顺序自动轮播；动画为 **1.1 倍速**，每项互动及气泡结束后间隔 **2 秒**。
- 所有完整皮肤使用自己的姿态播放动作，支持后续导入新皮肤；公开包附带蓝色、珊瑚色和薄荷色几何示例。
- 删除电脑小白板、转圈菜单和拖头画圈动作，头部使用普通拖动。
- 校园地图默认连接维护者提供的高德 / DeepSeek 公共服务，打开即可使用；历史与收藏保存在当前设备。

公共服务：[南京农业大学地图导航](https://littledeepseek-campus.nayoagan.chatgpt.site/)。服务可用性取决于网络和上游额度，私密 API 密钥不随源码或下载包公开。

## 作者的话（初版记录）

这是作者的处女作了，还有很多不好的地方希望各位能笑纳。

我是26年9月刚考上大学的，也是在暑假看了很多deepseek claude chatgpt grok等各种大模型的二创小短剧，也是喜欢上了可爱的拟人画风，所以就创作了蓝色大肥鱼的桌宠。

画一下饼：未来会添加换肤的功能，大家可以换上喜欢的小克、龙娘的皮肤，应该会很好看。

这个开源的桌宠是站在前人的肩膀上建立的，感谢：

- [MerZlin/dsh-pet-indesktop](https://github.com/MerZlin/dsh-pet-indesktop)
- [aklnaaw/dsh-xiaoke-widget](https://github.com/aklnaaw/dsh-xiaoke-widget)

感谢二位大佬的开源。
