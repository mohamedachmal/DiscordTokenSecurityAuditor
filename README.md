# Discord Token Security Auditor

A local-first VB.NET security-auditing starter project for finding likely Discord bot-token exposures in folders and Git repositories you own or are authorized to inspect.

## What is included

- **Core scanner (`net10.0`)** — shared scanning engine used by both front ends.
- **Command-line runner (`net10.0`)** — cross-platform; usable on Windows, Linux, and macOS where the .NET 10 runtime is available.
- **Windows desktop GUI (`net10.0-windows`)** — a simple WinForms interface with folder selection, optional Git-history inspection, findings grid, cancellation, and sanitized JSON export.

## Security behavior

The scanner is designed not to expose the credential it is looking for:

- It never prints a token, stores a token in a finding, calculates or exports a token hash, or includes source snippets in reports.
- It only reports metadata: severity, finding category, relative file path, line number, source (working tree / Git index / Git history), and remediation guidance.
- It performs no network requests and does not call Discord APIs. It cannot validate whether a token is active.
- Git history is inspected only when explicitly enabled. The scanner reads Git patch output in memory and inspects added lines; it does not write patch output to disk.
- It does not modify, delete, reset, or revoke credentials.
- Symlinks/reparse points are not followed. Common dependency and build folders are skipped. Individual files larger than 2 MiB and common binary file extensions are skipped.

The scanner uses heuristics, not a complete Discord token specification. A format match is a lead, not proof that a value is a genuine or active bot token. Conversely, a clean scan is not proof that a repository is secret-free.

## Requirements

- .NET 10 SDK to build the projects.
- Git installed and available on `PATH` for tracked-file checks and optional Git-history inspection.
- Windows for the WinForms desktop app. The core scanner and CLI are cross-platform.

.NET 10 is the current LTS line in this project and is supported through November 2028. See Microsoft's [release and support policy](https://learn.microsoft.com/en-us/dotnet/core/releases-and-support).

## Run the desktop GUI (Windows)

From this folder:

```powershell
dotnet run --project .\src\DiscordTokenSecurityAuditor.Desktop\DiscordTokenSecurityAuditor.Desktop.vbproj
```

Choose a project directory and press **Scan folder**. Enable **Also inspect Git history** when you need to check commits reachable from local branches and tags. This can take longer in large repositories.

Use **Export report** to save a JSON report. It contains finding metadata only, not the secret value.

## Run the CLI (Windows / Linux / macOS)

```bash
dotnet run --project ./src/DiscordTokenSecurityAuditor.Cli/DiscordTokenSecurityAuditor.Cli.vbproj -- scan ./path/to/your/repository
```

Include reachable Git history and export a sanitized report:

```bash
dotnet run --project ./src/DiscordTokenSecurityAuditor.Cli/DiscordTokenSecurityAuditor.Cli.vbproj -- scan ./path/to/your/repository --history --json ./audit-report.json
```

Exit codes:

- `0` — scan completed with no findings.
- `1` — scan completed and one or more potential findings were recorded.
- `2` — invalid arguments or scan failure.
- `130` — scan cancelled.

The CLI prints file paths and line numbers, but never matched values or source lines. Protect report files as internal security information because they reveal where a potential credential was found.

## What is detected

| Check | Meaning | Default severity |
|---|---|---:|
| Common three-part Discord token-like pattern | A line contains a value matching a commonly seen Discord token shape. | High |
| Literal value assigned to `DISCORD_TOKEN` / `DISCORD_BOT_TOKEN` (or contextual `BOT_TOKEN`) | A non-placeholder value is assigned to a Discord-related token variable, even if the token format is unfamiliar. | Medium |
| Token logging statement | A common print/logging call references a Discord token variable. | Medium |
| Tracked `.env` / `.env.*` file | Git's index contains a likely environment/secrets file (template/example files are excluded). | Medium |
| Git history | Same content checks applied to added lines in reachable commits when history mode is enabled. | Same as working tree |

## Secure storage recommendations

1. **Never commit real tokens.** Add local `.env` files to `.gitignore`; commit only a placeholder template such as `.env.example`.
2. **Prefer a secret manager** for production deployments and CI/CD. Use platform-native secret storage, restrict who can read it, and rotate credentials when staff or deployment access changes.
3. **For local development**, use an environment variable or an ignored local configuration file. Do not put the token in a frontend bundle, public repository, issue, screenshot, chat, or log.
4. **Avoid accidental disclosure paths** such as debug output, exception dumps, shell history, container image layers, build artifacts, and CI logs.
5. **Apply least privilege** to the bot's Discord permissions and to the systems that can read its secret.

Example `.gitignore` entries:

```gitignore
.env
.env.*
!.env.example
```

Adapt the exceptions if your repository intentionally uses non-secret environment templates. Check nested `.gitignore` rules and verify the files are actually untracked.

## If a bot token may have leaked

1. **Reset it immediately** in the Discord Developer Portal: open your application, select **Bot** in the side menu, choose **Reset Token** in the Build-a-Bot section, confirm, and complete 2FA if requested.
2. Replace the old token in every place the bot runs: local development, hosting, workers, containers, CI/CD secrets, and deployment configuration. Restart affected services.
3. Remove the secret from the current files and move it to a secret manager or protected environment variable.
4. If it was committed, treat it as compromised even after deleting the line or file. After rotating the token, clean the exposed value from Git history where appropriate, coordinate any force-push with collaborators, and account for forks, clones, caches, and CI artifacts. History rewriting does not make the old token safe again; rotation is the critical first step.
5. Review bot permissions, server activity, deployment logs, and relevant audit records for unexpected activity.

Discord's current [Developer Support guide](https://support-dev.discord.com/hc/en-us/articles/6470840524311-Why-can-t-I-copy-my-bot-s-token) documents the Bot settings > Reset Token flow. Discord's [Developer Terms](https://support-dev.discord.com/hc/en-us/articles/8562894815383-Discord-Developer-Terms-of-Service) also require developer credentials to be treated as confidential and not embedded in open-source projects.

## Scope and limitations

- Only scan folders and repositories you own or are authorized to inspect.
- No credential extraction from Discord accounts, browser profiles, sessions, memory, or third-party systems is performed or supported.
- No token is submitted to Discord, GitHub, a scanner API, or any third-party service.
- The history mode scans added lines in refs reachable by `git log --all`; it does not promise to find dangling/unreachable Git objects, encrypted archives, binary blobs, remote-only refs that have not been fetched, or content outside the selected folder.
- This is a starter auditor, not a replacement for a mature secret-scanning platform or a full secure-development lifecycle.

## Project structure

```text
DiscordTokenSecurityAuditor/
├── README.md
├── SECURITY.md
├── ARCHITECTURE.md
├── .gitignore
└── src/
    ├── DiscordTokenSecurityAuditor.Core/
    ├── DiscordTokenSecurityAuditor.Cli/
    └── DiscordTokenSecurityAuditor.Desktop/
```
