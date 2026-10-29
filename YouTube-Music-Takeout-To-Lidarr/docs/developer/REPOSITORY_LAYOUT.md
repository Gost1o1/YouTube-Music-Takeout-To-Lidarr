# Repository layout

This layout mirrors the organization used by
[Jellyfin Franchise Explorer](https://github.com/Gost1o1/Jellyfin-Franchise-Explorer).

```text
YouTube-Music-Takeout-To-Lidarr/            GitHub repository root
├── .github/
│   └── workflows/                          Windows build and release
├── .gitignore
├── README.md                                Download, walkthrough, help
├── LICENSE
├── SECURITY.md
└── YouTube-Music-Takeout-To-Lidarr/         Application project folder
    ├── AppUI/                               Local web UI and logo
    ├── src/                                 C# source
    ├── tools/                               Checks and upgrade helper
    ├── docs/
    │   ├── developer/                       Build, testing, layout
    │   └── history/                         Previous release notes
    ├── YouTubeMusicToLidarr.csproj
    └── BUILD-AND-RUN.ps1
```

The two folders have the same name by design, as in the Jellyfin repository:
the *outer* folder is the GitHub checkout, and the *inner* folder holds the
actual .NET project.

## Why the workflows stay outside

GitHub Actions discovers workflow files only from the repository's
`.github/workflows/` directory. Each workflow is configured with the nested
project as its working directory, then publishes its Windows artifact from
the nested `publish/` output.

If building locally, open PowerShell in the **inner project folder** and run
`BUILD-AND-RUN.ps1` or the .NET commands in [README](README.md).

For the one-time conversion of an older repository checkout, follow
[GITHUB-CLEANUP.md](GITHUB-CLEANUP.md). For ordinary updates after conversion,
copy the new source package into your existing GitHub Desktop checkout and
commit/push the changes; no additional restructuring is needed.
