# C# 代码规范

- `if`、`else`、`for`、`foreach`、`while`、`try`、`catch`、`finally` 的执行语句必须另起一行。
- `if`、`else`、`for`、`foreach`、`while`、`do` 等控制语句必须使用大括号，即使只有一条语句。
- 禁止 `if (condition) return;`、`try { Work(); }` 等压缩写法，也不要在同一行放多条语句。
- 大括号使用 Allman 风格，开始和结束大括号各自独占一行。
- 格式调整遵循根目录 `.editorconfig`，保持现有行为。
