# Codex SSH 密码登录包装器（Windows）

[English](README.md) | [简体中文](README.zh-CN.md)

这是一个**非官方**的 Windows OpenSSH 包装器，用于通过 Codex Desktop Remote SSH 连接采用密码认证的 Linux 主机。它适用于 Codex 使用 `-o BatchMode=yes` 启动 SSH、无法交互式输入密码的场景。

包装器**仅在参数同时包含** `.env` 中 `TARGET_HOST` 指定的 SSH 别名和 `BatchMode=yes` 选项时修改 SSH 参数。其他调用直接交给真实的 Windows OpenSSH 客户端，不修改参数，也不额外设置认证环境变量。真实客户端继承原有的 stdin、stdout 和 stderr 句柄，不通过托管流复制数据。

> **安全性：** 本方案从**本地明文 `.env` 文件**读取 SSH 密码。任何能够读取该文件的人都能获取密码。仅在远程主机的安全策略允许自动输入密码时使用。如果环境支持，优先使用密钥或其他非交互式认证方式。不要提交凭据或公开私人日志。
>
> **兼容性：** 本方案针对特定的 Codex Desktop SSH 调用方式。基础 SSH 测试通过，并不保证所有 Codex Desktop 版本都能建立长期运行的远程会话。

## 仓库内容

| 文件 | 用途 |
| --- | --- |
| `SshShim.cs` | 替代 `ssh.exe` 的包装器源码。 |
| `EnvConfig.cs` | 简单的共享 `.env` 读取器，分别编译进两个 EXE。 |
| `.env.example` | `TARGET_HOST`、`PASSWORD` 和 `REAL_SSH` 的配置模板。 |
| `CodexAskpass.cs` | 密码辅助程序 `codex-askpass.exe` 的源码。 |
| `build.ps1` | 使用 Windows PowerShell 5.1 在本地编译两个可执行文件。 |
| `ssh-config.example` | OpenSSH 主机别名配置示例。 |
| `.gitignore` | 排除密码、可执行文件、日志和编译文件。 |

运行时，生成的 `ssh.exe`、`codex-askpass.exe` 和你的私人 `.env` 必须放在同一文件夹中。

## 环境要求

- 安装了 OpenSSH 的 Windows；通过 `.env` 中的 `REAL_SSH` 配置其可执行文件的绝对路径。
- 使用 Windows PowerShell 5.1（`powershell.exe`）运行编译脚本，不要使用 PowerShell 7（`pwsh.exe`）。
- 已有 Linux 主机 SSH 账户，且主机允许你使用的认证方式。
- 支持 Remote SSH 的 Codex Desktop。

## 1. 本地编译

检查源码并克隆仓库，然后在仓库目录打开 **Windows PowerShell 5.1**：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\build.ps1"
```

脚本会将 `EnvConfig.cs` 分别编译进两个可执行文件，并在同一文件夹中生成 `ssh.exe` 和 `codex-askpass.exe`。编译不需要管理员权限。

## 2. 设置本地 `.env` 配置

复制并编辑模板：

```powershell
Copy-Item .\.env.example .\.env
notepad .\.env
```

`.env` 示例：

```dotenv
REAL_SSH=C:\Windows\System32\OpenSSH\ssh.exe
TARGET_HOST=codex-remote
PASSWORD="YOUR_SSH_PASSWORD"
```

`REAL_SSH` 必须是真实 OpenSSH 客户端的绝对路径，不能指向本包装器。缺少或无效的 `REAL_SSH` 会导致包装器以退出码 255 结束。`TARGET_HOST` 必须与本地 OpenSSH 配置中的别名一致。`PASSWORD` 直接填写 SSH 密码。

将 `.env` 保存为 UTF-8。值两侧配对的单引号或双引号会被移除；密码首尾包含空格时，请用引号包裹。`#`、`=`、`$` 和反斜杠等字符按字面值处理，不进行变量展开或转义。注释必须单独占一行，不要在值后面追加注释。不支持空密码或多行密码。

修改这些配置后**无需重新编译**：每个新进程都会读取其可执行文件旁的 `.env`，不受当前工作目录影响。辅助程序直接读取 `PASSWORD`，缺少该配置时不输出内容并退出。请妥善保管 `.env`；Git 已忽略该文件。

## 3. 配置主机别名

将以下示例添加到 `%USERPROFILE%\.ssh\config`，并替换主机名和用户名占位符：

```sshconfig
Host codex-remote
    HostName ssh.example.org
    User YOUR_REMOTE_USERNAME
```

别名必须与 `.env` 中的 `TARGET_HOST` 一致（示例为 `codex-remote`）。更改别名不需要修改源码或重新编译。

**对于负载均衡系统：** 如果远程 Codex app-server 在一台机器上启动，而后续 SSH 连接落到另一台机器，可能无法访问对应的本地 Unix socket。如果环境支持，请使用明确指定且经过授权的 SSH 端点。

## 4. 让包装器优先于 Windows OpenSSH

在 **Codex Desktop 进程**实际使用的 `PATH` 中，包装器文件夹必须位于 `C:\Windows\System32\OpenSSH\` **之前**。在无关的 PowerShell 窗口中修改 `PATH`，不会改变已经运行的桌面应用的环境变量。修改环境变量后，请重启 Codex。

在新打开的 PowerShell 窗口中检查：

```powershell
where.exe ssh
```

第一项应为刚编译的包装器，真实 Windows OpenSSH 客户端应排在后面。**不要覆盖或重命名系统的 SSH 可执行文件。**

如果只想在**当前 PowerShell 窗口中**测试，可在仓库目录执行：

```powershell
$env:Path = "$(Get-Location);$env:Path"
where.exe ssh
```

## 5. 测试并连接 Codex Desktop

```powershell
ssh -o BatchMode=yes codex-remote 'echo SHIM_OK; hostname'
'HELLO_FROM_WINDOWS' | ssh -o BatchMode=yes codex-remote 'cat'
```

第一条命令应在不提示输入密码的情况下输出 `SHIM_OK` 和远程主机名。第二条应回显 `HELLO_FROM_WINDOWS`，用于检查一次**短暂的** stdin/stdout 往返，不代表持续 WebSocket 连接测试。两项均通过后，重启 Codex Desktop，并选择 `codex-remote` 作为 Remote SSH 主机。

`ssh codex-remote`、`ssh -G codex-remote` 等普通 SSH 命令，以及连接其他别名的命令，保留真实 Windows OpenSSH 的行为。匹配要求 `TARGET_HOST` 别名作为一个完整参数出现，并使用受支持的 `BatchMode=yes` 写法；这**不是**通用 SSH 参数解析器。

## 工作原理

对于同时包含 `.env` 主机别名和 `-o BatchMode=yes`（或 `-oBatchMode=yes`）的调用，包装器会：

1. 从 `.env` 读取 `TARGET_HOST`；匹配后，移除原有的 `BatchMode=yes`，插入 `BatchMode=no` 及密码认证优先选项。
2. 将 `SSH_ASKPASS` 指向本地编译的辅助程序，并设置 `SSH_ASKPASS_REQUIRE=force` 和 `DISPLAY`。辅助程序直接从 `.env` 读取 `PASSWORD`。
3. 启动真实的 `ssh.exe`，继承原始标准句柄，**不进行流重定向或托管复制**；等待其结束，并返回其退出码。

包装器本身不建立网络连接，网络连接由真实 SSH 客户端处理。

## 故障排查

- `where.exe ssh` 首先显示系统 OpenSSH：调整 Codex Desktop 实际使用的 `PATH`，然后重启应用。
- 缺少辅助程序：编译两个可执行文件，并将它们放在同一文件夹中。
- 认证失败：检查 `.env` 是否存在、`TARGET_HOST` 是否与 SSH 别名一致、`PASSWORD` 是否为正确密码、`REAL_SSH` 是否指向真实客户端，以及主机允许的认证方式和系统 OpenSSH 路径。本方案不能绕过账户安全控制或多因素认证。
- 短命令正常，但 Codex 一直停留在 *Connecting*：检查 `%TEMP%\codex-ssh-shim.log`、Codex Desktop 的 Remote SSH 日志，以及远程 `~/.codex/app-server-control/app-server.log`。单行 `cat` 测试成功不能证明长期运行的代理连接正常。
- 包装器记录的是**被拦截的命令行**，而非 `.env` 中的密码；远程命令参数仍可能包含敏感信息。请妥善保管日志，并在分享前脱敏。

## 免责声明

本项目为非官方社区方案，未获 OpenAI 或任何 SSH 托管服务提供商背书。使用前请检查源码、确认符合所在平台的安全要求，并在自己的环境中测试。
