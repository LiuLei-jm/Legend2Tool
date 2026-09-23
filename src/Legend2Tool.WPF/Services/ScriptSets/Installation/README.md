# 脚本套部署内部组件

`ScriptSetInstallationService` 是对外入口，负责安装/卸载顺序、取消、结果统计和失败恢复。
本目录的类型均为内部实现，不供 ViewModel 直接调用。

| 组件 | 职责 |
| --- | --- |
| `DeploymentPathResolver` | 脚本、素材和 PAK 路径定位及目录边界检查 |
| `ScriptFileDeployment` | 脚本计划、全量写入、卸载前校验和文件移除 |
| `ScriptSegmentEditor` | 触发器与标记片段的字节级插入/删除，不执行文件 I/O |
| `MaterialDeployment` | 素材下载暂存、校验、部署和临时目录清理 |
| `PakFileEditor` | PAK 条目的追加与删除 |
| `DeploymentFileStore` | 编码/BOM、原子文件操作、快照与恢复 |
| `Database/ScriptSetDatabaseDeployment` | 数据行解析、引擎映射及数据库实现选择 |
| `Database/IScriptSetDatabase` | SQLite / Access 的结构检查、事务写入和删除边界 |
| `Database/ScriptSetDatabaseRules` | 两种数据库共用的字段、编号与表名规则 |
| `Plans` | 单次操作中传递的内部计划，不作为长期共享状态 |

## 必须保持的执行约束

1. 修改目标文件前保存快照；素材暂存目录同时承载原素材的恢复副本。
2. 先修改脚本、素材和 PAK，再提交数据库事务。每次数据库操作的全部记录属于同一个事务。
3. 数据库或文件操作失败时，由入口恢复文件；清理暂存目录必须在恢复流程结束后执行。
4. 文件恢复不得使用已经取消的操作令牌，避免取消操作阻止恢复。
5. SQLite / Access 连接、事务和编号分配缓存均属于单次调用，不存入服务字段。
6. 保留原文件编码、BOM、换行和未修改字节；未知编码仍回退到 GB18030。

当前方案是数据库事务加文件补偿恢复，并非跨文件和数据库的统一事务。
卸载仍依赖远程部署数据；数据库删除仍按非 Idx 字段匹配。安装清单、操作级配置快照、
恢复失败后保留备份等改进应单独实施，不与职责拆分混合。

## 验证

从仓库根目录运行 `dotnet test Legend2Tool.sln`。
安装集成测试覆盖素材准备失败、编码/BOM、多行写入失败、卸载失败恢复和取消。
数据库测试覆盖全部引擎映射和三种数据表的部署；Access 实测还覆盖唯一约束失败和取消回滚。
Access 测试需要同位数的 ACE OLEDB 12.0、ADOX 和 VBScript，缺少时明确跳过。
测试只操作临时目录与临时数据库，ADOX 创建在独立进程中进行。
