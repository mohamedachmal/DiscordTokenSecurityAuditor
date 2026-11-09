# Security notes

## Data handling

The scanner's result model deliberately has no field for matched text, token fragments, hashes, or source snippets. Findings are reduced to metadata before leaving the line-inspection routine. The JSON exporter serializes only the sanitized result model.

The scanner necessarily reads source text into process memory while examining it. It does not persist or transmit that source text. For especially sensitive environments, run it on a trusted endpoint and review the source code before scanning.

## Reporting a suspected exposure

Treat any genuine token found in a working tree, commit, artifact, or log as compromised. Rotate it in Discord first. Then replace it everywhere the bot is deployed, remove the exposure from current files and history, and review the bot's permissions and activity.

## Safe use

Use this tool only on local files and repositories you own or have explicit authorization to inspect. Do not adapt it to search other people's computers, browser profiles, Discord sessions, or account data.

## Known limitations

Pattern-based detection can miss changed token formats and can produce false positives. Files over 2 MiB, common binary formats, symlinked paths, and common dependency/build directories are skipped. Git history scanning reads additions in refs visible through `git log --all`; it is not a forensic scan of every object in a repository database.
