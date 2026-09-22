# 候选入口证据

本目录对应[候选验收](../agent-platform-candidate-2026-09-20.md)的干净来源 `bc59db8de897261c3385286baf2e6cedf6b9677b`。新增提供方调用为 0。

| 文件 | 内容 |
| --- | --- |
| [tests/manifest.json](tests/manifest.json) | 12/12 PlayMode 结果、来源、原始日志与公开 XML 的 SHA256 |
| [运行前记录](tests/candidate-entry-preflight.json) | 干净检出、LFS、场景身份、关闭提供方和本轮运行前已有包目录 |
| [source-and-settings.json](source-and-settings.json) | 与集成实测相同的四棵 Git 树；Unity 本轮生成设置的处理及诊断 SHA256 |
| [依赖审计](candidate-dependency-audit.json) | 读取当时的 27 个 Resolution、41 条阻塞边、28 个祖先 PR head；不把最后交付票据提前记为关闭 |
| [入口实验包](candidate-entry-packages.zip)及[清单](candidate-entry-packages.manifest.json) | 本轮新产生的 8 个包、29 个文件，按原字节保存；逐文件 SHA256、测试方法和样本分类 |

XML 仅把本机项目、用户和 Unity 安装前缀替换为占位符，名称及结果计数未改。完整原始日志留在本地，由清单中的 SHA256 标识。依赖审计为原 JSON 的公开副本；最终 GitHub 状态以交付票据 Resolution 为准。

包内 `sourceRevision` 的 `test-build`／`unknown` 是测试夹具声明，本目录以外部运行前记录绑定实际源码，没有将夹具改写成真实构建证明。`a7d41a9a4bb546fead47239fc1dc0e6a` 使用模拟 HTTP 度量，虽然标为 `live`，仍是 0 提供方调用；`88cebb44f31b430daf32d2db3b1d200f` 故意修改检查点验证拒绝，不能作为成功回放示例。

需要检查正向回放时，可解压并选择 `e5a85eec41ea4d6d86b1f26c0545bc95`（S 保存／读取／交付）或 `341acffc9d504736be8818a1e9a44e8f`（F 跨日）；在实验窗口 Replay 选择对应含 `manifest.json` 的目录。原测试已在新场景回放这些包，用户再次回放产生新的运行身份。

ZIP SHA256：`f5064c81d1ec928a91877e00669499e047cd1e549b824fa2cacdc5a9813e66d3`，大小 371633 字节。
