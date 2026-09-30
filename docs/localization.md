# TrackSwap 本地化

TrackSwap 的官方界面语言是简体中文。社区语言包使用纯 JSON 文件，放在当前用户数据目录的 `i18n` 子目录中：

```text
TrackSwap\UserData\i18n
```

如果安装目录不可写或仍在使用旧版数据，实际位置会随活动数据目录变为 `%LOCALAPPDATA%\TrackSwap\i18n`。可在“设置 → 语言”中查看并直接打开准确位置。

## 创建语言包

1. 在“设置 → 语言”中单击“导出翻译模板”。
2. 选择保存位置并编辑导出的 JSON。
3. 修改 `locale`、`displayName` 和 `author`。
4. 把 `strings` 中的中文值替换为目标语言；不要修改键名。
5. 把完成的 JSON 放入语言包目录，再单击“刷新语言”。

语言包格式：

```json
{
  "schemaVersion": 1,
  "locale": "en-US",
  "displayName": "English",
  "author": "",
  "targetTrackSwapVersion": "v011",
  "strings": {
    "route.empty": "No configurations. Create one to begin."
  }
}
```

`strings` 始终是扁平的键值表，不按页面嵌套 JSON 对象。键名使用小写点分语义结构，形如 `settings.language.refresh` 或 `route.status.pending_apply`；不使用哈希、`ui.auto.*` 或不透明的数字尾缀。文件按键名排序，不同的顶级功能前缀之间留一个空行，仅改善阅读，不改变扁平结构。

语言包使用标准 JSON，不支持注释。字符串的使用场景将放在 Crowdin 上下文或独立工具元数据中，不混入运行时 JSON。如果原文含 `{0}`、`{1}` 等占位符，翻译后必须完整保留。

TrackSwap 不执行语言包中的任何代码，也不加载 XAML、DLL 或脚本。单个语言包最大 1 MiB，单条译文最长 1024 个字符，同一 locale 只能由一个文件提供。

## 检查翻译

翻译页会列出：

- `缺失`：模板中存在，但语言包没有提供该键。
- `无效`：翻译为空或含不支持的控制字符。
- `已失效`：语言包含有当前版本已不存在的键。

缺失或无效翻译不会回退成中文，而会直接显示成 `⟦key.name⟧`。单击界面中的缺失键或翻译检查列表即可复制原始键值，方便在 JSON 中搜索。

独立的 TrackSwap VR 视图会在打开时读取主界面当前选中的语言；语言包刷新后，重新打开 VR 视图即可应用新文案。

日志和底层诊断文本统一使用英文，不属于本地化资源。

## Crowdin 协作

Crowdin 使用仓库根目录的 `crowdin.yml`。上传到 Crowdin 的源文件是 `localization/crowdin/source.csv`，它仅是由官方简体中文目录生成的交换文件，不是第二份文案真源。CSV 中包含语义键、中文原文、独立的空译文列和来自源码引用位置的英文上下文。不要把源文和译文配置为同一个 `source_or_translation` 列。该文件组必须同时启用 `skip_untranslated_strings: true` 与 `export_only_approved: true`：未翻译或尚未批准的内容不能进入交付语言包，也不能由 Crowdin 用中文源文回填译文列。

更新中文目录后，在仓库根目录运行：

```powershell
./scripts/Export-CrowdinSource.ps1
```

Crowdin 导出的翻译 CSV 位于 `localization/crowdin/translations/%locale%.csv`。将它转换为 TrackSwap 运行时语言包：

```powershell
./scripts/Import-CrowdinTranslation.ps1 `
  -InputPath ./localization/crowdin/translations/en-US.csv
```

转换器只读取独立的 `translation` 列，并会拒绝过期键、重复键以及 `{0}` 等占位符不一致的译文；未翻译键会被省略并发出警告。即使译文恰好与中文源文完全相同，只要 Crowdin 将它写入译文列，转换器也会保留。生成的 JSON 默认位于 `localization/packs`。

## 面向开发者和自动化代理的硬约束

- 内嵌的 `zh-CN.json` 是唯一官方源文目录。新增界面文案时，先添加稳定的语义键，再在 XAML 或代码中引用；不要在界面层重新维护一份中文常量。
- 社区语言包只能是活动数据目录 `i18n` 下的纯 JSON。不得从语言包加载代码、XAML、程序集或脚本。
- 缺失、空白或格式错误的译文必须显示 `⟦key⟧`；未知键报告为已失效。不要静默回退为中文。界面上出现的回退键应便于单击复制。
- 运行日志和底层诊断统一使用英文，不进入本地化目录。
- 文件只有一个扁平 `strings` 对象，值直接是译文字符串；不得嵌套分组，也不得使用 `{ "source", "translation" }` 包装。键名使用稳定、小写、点分的 `feature.area.purpose` 语义结构；禁止哈希、`ui.auto.*` 和不透明数字尾缀。按序数排序键，并在顶级功能前缀之间留一个空行。
- 翻译必须完整保留 `{0}`、`{1}` 等复合格式占位符。
- `localization/crowdin/source.csv` 只能通过 `scripts/Export-CrowdinSource.ps1` 生成，不得手工编辑，也不得让 Crowdin 回写官方中文目录。
- Crowdin CSV 必须把源文与译文分别放在 `source_phrase` 和 `translation` 列，禁止 `source_or_translation`。
- `crowdin.yml` 的文件组和 Crowdin 项目级导出设置都必须启用 `skip_untranslated_strings` 与 `export_only_approved`，避免中文源文或未批准译文进入语言包。
- Crowdin 返回的 CSV 必须通过 `scripts/Import-CrowdinTranslation.ps1` 转换并校验后才能成为运行时 JSON。
- Crowdin 项目 ID、访问令牌及其他凭据不得进入版本库；CLI 凭据使用环境变量，GitHub 集成凭据由 Crowdin/GitHub 保存。
