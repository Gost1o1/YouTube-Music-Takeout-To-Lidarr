# v0.9.0.3 GitHub update note

This source ZIP fixes the read-only Lidarr inventory endpoint 400 error by adding required numeric `artistId` filters to track/trackfile queries. Confirm a successful Windows build, self-test, and comparison before publishing a public release. Review the project diff, commit only source, and keep the user's private Takeout, audits and API keys out of the repo.

---

# Preparing the v0.9.0 update to GitHub

Repository: https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr

This source ZIP has **not** been pushed, built on Windows, or released. Make a backup/commit of existing files before replacing them.

```powershell
git clone https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr.git
cd YouTube-Music-Takeout-To-Lidarr
git checkout -b feature/v0.9.0-independent-catalogs
# Copy v0.9.0 source into this repo, preserving the .git folder.
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
dotnet run --project YouTubeMusicToLidarr.csproj -c Release --no-build -- --self-test
git status
git diff --check
git add Program.cs Verification.cs Audit.cs SupplementalEvidence.cs YouTubeMusicToLidarr.csproj AppUI README.md RELEASE-NOTES.md SECURITY.md TESTING.md GITHUB-UPDATE.md SHA256SUMS.txt .gitignore .github/workflows/build-windows.yml
git commit -m "Add optional independent catalog evidence and prior audit import"
git push -u origin feature/v0.9.0-independent-catalogs
```

Open a pull request, wait for GitHub Actions Windows build and offline self-tests to pass. Only then consider a test release. Do not include original Google Takeout ZIPs, API keys, Lidarr configs, private audit CSVs or `publish/` in Git.

Expected report fields are described in `README.md` and `TESTING.md`. The Windows CI artifact is a **development build**, not yet a final standalone one-file portable executable.
