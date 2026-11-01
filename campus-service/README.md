# 公共校园地图服务

官方桌宠默认使用 https://littledeepseek-campus.nayoagan.chatgpt.site/ ，终端用户无需填写 API 密钥。
服务使用项目维护者提供的高德与 DeepSeek API；密钥保留在托管服务的秘密变量中，源码和下载包均不包含私钥。

`worker/` 是无登录地图前端及 Worker 服务；30 个校园点位来自 [NJAU-Map](https://github.com/nayoagan-ship-it/NJAU-Map)，保留其许可文件。
历史与收藏仅保存在当前设备的浏览器存储中，不共享本机原有数据库。

## 维护者自行部署

使用支持标准 Worker fetch 接口的托管平台。设置 `.env.example` 中四个运行时变量；高德浏览器 key 是应用标识，安全码通过服务端代理使用。
在本地 `.openai/hosting.json` 写入自己站点的 project_id，d1 和 r2 均可为 null。该文件属于部署配置，不提交到开源仓库。

```text
node scripts/build.mjs
node scripts/validate-artifact.mjs
node scripts/test.mjs
```

部署输出为 `dist/server/index.js`。普通桌宠使用者不需要部署这些文件。

接口有校园地点范围约束、输入长度及 token 上限、每实例每 IP 短期限频；限频是尽力而为，不是全局费用上限。维护者可在托管平台修改或移除运行时密钥暂停服务。

实现参考：[高德安全码服务端代理](https://lbs.amap.com/api/javascript-api-v2/guide/abc/jscode)、[DeepSeek JSON 输出](https://api-docs.deepseek.com/guides/json_mode/)。
