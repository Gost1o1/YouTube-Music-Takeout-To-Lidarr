# One-time GitHub repository reorganization

The source ZIP now follows the same top-level layout as
**Jellyfin-Franchise-Explorer**: the root holds the README, license, security
policy and GitHub Actions, while all application source, UI, scripts and
history live in `YouTube-Music-Takeout-To-Lidarr/`.

Simply uploading the new folder through the GitHub website will **not remove**
old `src/`, `AppUI/`, `docs/`, `tools/`, `BUILD-AND-RUN.ps1` or
`YouTubeMusicToLidarr.csproj` from the root. That produces duplicates and may
break the build. Do the one-time reorganization using a local Git clone.

## Recommended GitHub Desktop method

1. In **GitHub Desktop**, clone `Gost1o1/YouTube-Music-Takeout-To-Lidarr`
   (or open your existing clean clone).
2. Verify GitHub Desktop shows **no uncommitted changes**. Commit or preserve
   your own pending edits before using the helper.
3. Extract the organized source ZIP into a **separate folder**, not inside the
   GitHub clone.
4. Open PowerShell in the extracted ZIP's **outer folder** and run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\YouTube-Music-Takeout-To-Lidarr\tools\Apply-GitHub-Cleanup.ps1 -RepositoryPath 'C:\path\to\your\GitHub\clone'
   ```

5. Open GitHub Desktop and inspect **Changes**. The old root folders and
   project files will be *moved* into `YouTube-Music-Takeout-To-Lidarr/`;
   the new README and root `.github/workflows/` are copied in.
6. Commit with a message such as `Organize Lidarr source like Jellyfin repo`
   and select **Push origin**.
7. Confirm **Actions → Build and smoke-test Windows .NET** passes. Only then
   publish a new public Release.

The helper does not commit, push, publish, delete unrelated files or erase
Git history. It refuses to run on a clone with uncommitted changes or with
conflicting old and nested paths. Renamed source files remain recoverable in
Git history. You may instead make the same moves manually in GitHub Desktop.

**Important:** Do not upload the ZIP itself *as source*. GitHub does not
automatically extract an uploaded ZIP. Keep `.github/workflows/` at the root,
not inside the nested project folder.
