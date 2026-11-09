# Architecture and trust boundaries

```text
┌───────────────────────────┐      ┌───────────────────────────┐
│ WinForms desktop frontend │      │ Cross-platform CLI        │
│ folder picker / results   │      │ scan / exit code / JSON   │
└──────────────┬────────────┘      └──────────────┬────────────┘
               └──────────────────┬───────────────┘
                                  ▼
                     ┌────────────────────────┐
                     │ Shared VB.NET Core     │
                     │ TokenScanner           │
                     └────────────┬───────────┘
                                  ▼
              ┌──────────────────┼───────────────────┐
              ▼                  ▼                   ▼
     Working-tree text     Git index paths      Optional Git patches
     streamed by line      names only           added lines streamed
              └──────────────────┼───────────────────┘
                                  ▼
                     ┌────────────────────────┐
                     │ Detection / reduction  │
                     │ discard matched text   │
                     └────────────┬───────────┘
                                  ▼
                     ┌────────────────────────┐
                     │ Sanitized findings     │
                     │ path / line / type     │
                     └────────────┬───────────┘
                                  ▼
                     GUI grid / CLI / JSON
```

## Trust boundaries

- **File content:** read only from the selected directory. Content is examined in memory one line at a time; it is never copied into the result model.
- **Git:** only `git ls-files` and, when opted in, `git log` are invoked. External diff and text-conversion helpers are disabled, the pager is disabled, and the repository fsmonitor hook is explicitly disabled for these commands.
- **Network:** none. The scanner does not make HTTP requests and does not validate credentials with Discord.
- **Output:** findings contain metadata only. The report writer serializes only the sanitized result object.

The user should still run scans on a trusted machine. Any security scanner that examines source files necessarily handles their contents transiently in process memory.
