# Codex SSH Password Shim (Windows)

An **unofficial, source-only** Windows OpenSSH wrapper for using Codex Desktop Remote SSH with a password-authenticated Linux host. It is intended for situations where Codex starts SSH with `-o BatchMode=yes` and cannot prompt for a password.

The wrapper changes SSH arguments **only** when it sees both the SSH alias configured by `TARGET_HOST` in `.env` and the option `BatchMode=yes`. Everything else is handed to the real Windows OpenSSH client without modified arguments or additional authentication environment variables. It starts the real client with inherited stdin, stdout and stderr handles rather than copying data through managed streams.

> **Security:** This workaround reads your SSH password from a **local plaintext `.env` file**. Anyone who can read that file can obtain your password. Use it only if your remote host's security policy allows password automation. A supported key-based or other non-interactive authentication method is preferable when available. Never commit credentials or publish private logs.
>
> **Compatibility:** This is a workaround for a particular Codex Desktop invocation pattern. Basic SSH tests do not guarantee that every Codex Desktop release will establish a long-lived remote session.

## Repository contents

| File | Purpose |
| --- | --- |
| `SshShim.cs` | Source of the replacement `ssh.exe` wrapper. |
| `EnvConfig.cs` | Shared minimal `.env` reader, compiled into both EXEs. |
| `.env.example` | Template for `TARGET_HOST`, `PASSWORD`, and `REAL_SSH` configuration. |
| `CodexAskpass.cs` | Source of the `codex-askpass.exe` password helper. |
| `build.ps1` | Builds both executables locally with Windows PowerShell 5.1. |
| `ssh-config.example` | Sample OpenSSH host-alias configuration. |
| `.gitignore` | Excludes passwords, executables, logs and build files. |

**No compiled executables or real credentials are distributed.** At runtime, the generated `ssh.exe`, `codex-askpass.exe`, and your private `.env` must be in the same folder. No separate password file is needed.

## Requirements

- Windows with OpenSSH; configure its absolute executable path using `REAL_SSH` in `.env`.
- Windows PowerShell 5.1 (`powershell.exe`) for the included build script. Do not use PowerShell 7 (`pwsh.exe`) with this script.
- An existing SSH account for a Linux host that accepts your authentication method.
- Codex Desktop with Remote SSH support.

## 1. Build locally

Review the source, clone the repository and open **Windows PowerShell 5.1** in its directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\build.ps1"
```

This compiles `EnvConfig.cs` into both executables and produces `ssh.exe` and `codex-askpass.exe` in the same folder. Compiling does not require administrator privileges.

## 2. Set the local `.env` configuration

Copy the template and edit it:

```powershell
Copy-Item .\.env.example .\.env
notepad .\.env
```

Example `.env`:

```dotenv
TARGET_HOST=codex-remote
PASSWORD="YOUR_SSH_PASSWORD"
REAL_SSH=C:\Windows\System32\OpenSSH\ssh.exe
```

`REAL_SSH` must be an absolute path to the real OpenSSH client, not this wrapper. Missing or invalid `REAL_SSH` causes the wrapper to exit with code 255. `TARGET_HOST` must equal an alias in your local OpenSSH config. `PASSWORD` is the SSH password itself. 

Save `.env` as UTF-8. Matching single or double outer quotes are removed; quote passwords to preserve leading/trailing spaces. Characters such as `#`, `=`, `$`, and backslashes are literal: no variable expansion or escape processing is performed. Comments must occupy their own lines; do not append comments after values. Empty or multiline passwords are unsupported.

Changing these settings does **not** require rebuilding: each new process reads `.env` beside its executable, regardless of the working directory. The helper reads `PASSWORD` directly and exits without output if it is missing. Keep `.env` private; it is Git-ignored. 

## 3. Configure a host alias

Add the example entry to `%USERPROFILE%\.ssh\config`, replacing the placeholder hostname and username:

```sshconfig
Host codex-remote
    HostName ssh.example.org
    User YOUR_REMOTE_USERNAME
```

The alias must match `TARGET_HOST` in your `.env` (the example uses `codex-remote`). There is no need to edit source code or rebuild when changing the alias.

**For load-balanced systems:** If the remote Codex app-server starts on one machine but later SSH connections land on another, the local Unix socket may not be reachable. Use a specific, authorised SSH endpoint if your environment supports it.

## 4. Place the wrapper before Windows OpenSSH

The wrapper folder must appear **before** `C:\Windows\System32\OpenSSH\` in the effective `PATH` of the **Codex Desktop process**. Changing `PATH` in an unrelated PowerShell window will not change the environment of an already-running desktop app. Restart Codex after editing your environment.

Check in a fresh PowerShell window:

```powershell
where.exe ssh
```

The first entry should be your newly built wrapper; the real Windows OpenSSH client should appear later. **Do not overwrite or rename the system SSH executable.**

For testing in *this PowerShell window only*, from the repository directory:

```powershell
$env:Path = "$(Get-Location);$env:Path"
where.exe ssh
```

## 5. Test, then try Codex Desktop

```powershell
ssh -o BatchMode=yes codex-remote 'echo SHIM_OK; hostname'
'HELLO_FROM_WINDOWS' | ssh -o BatchMode=yes codex-remote 'cat'
```

The first command should print `SHIM_OK` and a remote hostname without a password prompt. The second should echo `HELLO_FROM_WINDOWS`; it checks a **short** stdin/stdout round trip, not a sustained WebSocket connection. After both pass, restart Codex Desktop and choose `codex-remote` as the Remote SSH host.

Ordinary SSH commands such as `ssh codex-remote`, `ssh -G codex-remote`, and commands targeting other aliases retain the real Windows OpenSSH behavior. The match uses the `TARGET_HOST` alias as an exact argument token and supported `BatchMode=yes` forms; it is **not** a general SSH argument parser.

## How the wrapper works

For the `.env` host alias and `-o BatchMode=yes` (or `-oBatchMode=yes`), it:

1. Reads `TARGET_HOST` from `.env`; for matching calls, removes the original `BatchMode=yes` and inserts `BatchMode=no` and password-authentication preferences.
2. Sets `SSH_ASKPASS` to the locally built helper, which reads `PASSWORD` directly from `.env`, along with `SSH_ASKPASS_REQUIRE=force` and `DISPLAY`.
3. Starts the real `ssh.exe` with the original standard handles inherited **without stream redirection or managed copying**, waits for it to finish, and forwards its exit code.

The wrapper itself does not open network connections; the real SSH client does.

## Troubleshooting

- `where.exe ssh` shows system OpenSSH first: adjust the effective `PATH` used by Codex Desktop and restart it.
- The helper is missing: build both executables and keep both generated executables together.
- Authentication fails: check `.env` exists, `TARGET_HOST` matches your SSH alias, `PASSWORD` contains your SSH password and `REAL_SSH` points to the real client, your allowed authentication method and system OpenSSH location. This is not a way to bypass account security controls or multi-factor authentication.
- Short commands work, but Codex remains at *Connecting*: inspect `%TEMP%\codex-ssh-shim.log`, Codex Desktop's Remote SSH logs and remote `~/.codex/app-server-control/app-server.log`. A successful one-line `cat` test does not prove the long-lived proxy works.
- The wrapper logs **intercepted command lines**, not the `.env` password; remote command arguments can still be sensitive. Keep the log private and redact it before sharing.

## Disclaimer

Unofficial community workaround. Not endorsed by OpenAI or any SSH hosting provider. Review the source, validate your platform's security requirements and test it against your own environment before using it.
