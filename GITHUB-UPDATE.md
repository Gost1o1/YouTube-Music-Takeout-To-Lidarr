# Publishing v0.8.5 to GitHub

Repository: https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr

To update **your existing local clone**, copy the content of this source folder into the repository root (without copying `bin/`, `obj/`, or `publish/`). Review the diff, then from the repository root:

```powershell
git checkout -b feature/v0.8.5-confidence-audit
git status
git add Program.cs Verification.cs Audit.cs YouTubeMusicToLidarr.csproj AppUI/index.html README.md RELEASE-NOTES.md SECURITY.md LICENSE GITHUB-UPDATE.md
git commit -m "Add separate artist and song confidence audits"
git push -u origin feature/v0.8.5-confidence-audit
```

Open a pull request from that branch to your default branch. **Do not push until the Windows publish command succeeds and the changes have been reviewed.** Don't commit binaries, API keys, Takeout archives, logs, or CSV exports containing your private library.

Suggested pull request title: `v0.8.5: evidence-based artist and song verification audit`

Suggested description:

> Adds separate artist and recording confidence classifications, per-record evidence and artist/song-list CSV exports, and explicit partial MusicBrainz catalog-coverage reporting. Keeps the read-only .NET workflow, original wizard interface, and fresh batched verification. Does not import any data to Lidarr. Needs a Windows compile and real-run smoke test before merge.

This package does not automatically push code to GitHub.
