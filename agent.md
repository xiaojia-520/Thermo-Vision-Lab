# 编码规则

- 所有文件读写默认使用 UTF-8。
- 修改文件时不得改变原有编码、换行风格和无关内容。
- 读取中文文件前，PowerShell 先执行 `chcp 65001`。
- PowerShell 中设置：
  - `[Console]::OutputEncoding = [System.Text.Encoding]::UTF8`
  - `$OutputEncoding = [System.Text.Encoding]::UTF8`
- 读取中文文件优先使用 `Get-Content -Raw -Encoding UTF8`。
- 禁止使用 PowerShell here-string、重定向、`Set-Content`、`Out-File` 写入中文源码、JSON 或 Markdown。
- 禁止用 `sed`、`awk` 处理中文文件。
- 涉及中文内容的批量修改，必须使用 Python 或 Node.js，并显式指定 UTF-8。
- 不要为了修编码而整文件重写、全文格式化或全文字符串替换。