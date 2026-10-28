# One-time repository cleanup

The new source ZIP is already organized into `src/`, `docs/`, `AppUI/`,
`tools/` and `.github/`. Uploading it over the GitHub website **does not
remove the original root files**, so you would still see duplicate C# sources
and old changelogs in the repository.

Use a local clone to make the rename operations in one reviewed Git commit.

1. Open **GitHub Desktop**, choose **File → Clone repository** and clone
   `Gost1o1/YouTube-Music-Takeout-To-Lidarr`.
2. Make sure the clone shows **no uncommitted changes**.
3. Extract the new complete-source ZIP into a separate folder. Do **not**
   extract it on top of the clone.
4. Open PowerShell in the **extracted ZIP folder** and run the command below,
   replacing the example path with the folder shown by GitHub Desktop under
   **Repository → Show in Explorer**:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\tools\Apply-GitHub-Cleanup.ps1 -RepositoryPath 'C:\path\to\YouTube-Music-Takeout-To-Lidarr'
   ```

5. Review the changes in **GitHub Desktop**. The cleanup helper moves known
   root files to `src/` and `docs/`, copies in the new README and workflows,
   and preserves unrelated files. It does **not** commit or push changes.
6. Commit with a message such as `Organize source, archive history, and simplify README`.
7. Click **Push origin**, then open **Actions** on GitHub and confirm the
   Windows workflow passes. Only after that should you manually publish a Release.

The script requires Git on `PATH`, checks that the clone is clean first,
refuses to overwrite a destination already containing a conflicting moved
file, and leaves any unrelated root files alone. It also moves an existing
`IMPLEMENTATION-PLAN.md` into `docs/developer/` without losing its contents.

The previous file layout stays recoverable in Git commit history. The helper
does not delete user data, publish a release, or alter the running app.
