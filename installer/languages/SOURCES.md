# Inno Setup 语言文件来源

## 繁體中文

`ChineseTraditional.isl` 直接取自 Inno Setup 官方 `jrsoftware/issrc` 倉庫，保留上游檔案與作者註記，未修改翻譯內容。

- 上游固定提交：`6ef32198ef1f7b7b375cd4b6b90896c2a58eb4c2`
- 上游路徑：`Files/Languages/ChineseTraditional.isl`
- 原始檔案：https://raw.githubusercontent.com/jrsoftware/issrc/6ef32198ef1f7b7b375cd4b6b90896c2a58eb4c2/Files/Languages/ChineseTraditional.isl
- SHA-256：`031684fc769259291fd563338b5abe20b7753c88ab5a2976b83a80788deb8455`
- 取得日期：2026-09-14
- 上游作者註記包含 GoneTone、Anbang LI、Enfong Tsao、Samuel Lee；完整註記見語言檔案開頭。

檔案宣告適用 Inno Setup 6.5.0 以上版本。與本專案 Inno Setup 6.7.3 的 `Default.isl` 比對，281 個 `[Messages]` 鍵完全對應。安裝器專用的繁體中文 `[CustomMessages]` 位於 `../WebDisplay.iss`。

既有簡體中文檔案在本次更新中未變更。英文使用 Inno Setup 隨附的 `Default.isl`。Inno Setup 授權條款見 `../INNO-LICENSE.txt`。

## 日本語 / 한국어

`Japanese.isl` 和 `Korean.isl` 于 2026-09-28 从本项目现有的 Inno Setup 6.7.3 官方工具目录 `.tools/inno-setup/Languages` 直接复制。未重新下载，未修改文件内容或作者说明；语言文件一并纳入源码包，不要求构建者另外寻找翻译文件。

| 文件 | 来源目录内路径 | SHA-256 |
| --- | --- | --- |
| `Japanese.isl` | `Languages/Japanese.isl` | `d5450537bb128112347bf86a4bdc3a4be0605414df0bc4fc90f55aaef1ba369b` |
| `Korean.isl` | `Languages/Korean.isl` | `cb56d6ea5c082bcf6b4acc1ae4c303ba91f662b1d0bfe05e4d3902c38c41e02a` |

两份文件均声明适用 Inno Setup 6.5.0 以上版本。日语文件保留 Koichi Shirasuka、Ryou Minakami 的作者说明；韩语文件保留 VenusGirl、Logan.Hwang、SungDong Kim、Domddol 的贡献说明。完整说明见文件开头。

与本项目 `Default.isl` 比较，韩语包含全部 281 个 `[Messages]` 键；日语包含 280 个，仅省略默认值本来就为空的可选 `HelpTextNote`；安装脚本在 `[Messages]` 中显式补为空，保持官方默认行为。已有消息的编号参数均一致。每种新增安装语言的 13 条应用专用 `[CustomMessages]` 在 `../WebDisplay.iss` 中完整提供。前三种安装语言及安装、升级、卸载行为不变。

Inno Setup 授权条款见 `../INNO-LICENSE.txt`；官方项目为 [jrsoftware/issrc](https://github.com/jrsoftware/issrc)。