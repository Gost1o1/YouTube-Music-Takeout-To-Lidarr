# Optional in-app GitHub star: developer setup (v0.9.1.8)

This is a one-time **project maintainer** setup. End users do not need to paste tokens, provide credentials to the app, or visit the repository page. They must still visit GitHub's official device-login page once to authorize access to their GitHub account.

1. Create a **GitHub App**, NOT a personal access token, at GitHub Settings > Developer settings > GitHub Apps. For example, name it `YouTube Music Takeout to Lidarr Star`. Set homepage to `https://www.gost1o1.pro`. You may need a callback URL as required by the GitHub App registration UI.
2. Under **Permissions**, grant only **Starring: Read & write (Account/User permissions)**; keep repository permissions at **Metadata: Read**. Do not request Contents, Issues, user emails, repository administration, or organization access.
3. Enable the **Device Flow** in the app's optional settings. Save the app.
4. Copy its **Client ID** (for GitHub Apps, generally `Iv1.xxxxx...`). It is a public identifier, **not** a client secret.
5. On Windows PowerShell, before launching the local application, run:

   ```powershell
   $env:GOST1O1_GITHUB_CLIENT_ID = 'Iv1.REPLACE_WITH_YOUR_REAL_GITHUB_APP_CLIENT_ID'
   .\publish\YouTubeMusicToLidarr.exe
   ```

   Or configure the same environment variable in your packaging/launch process. The variable must be visible **to the process** that launches the EXE. Do not put a GitHub client secret, API token, or private key into the source or into this variable.

6. On the completion/About page, **Connect GitHub to Star** starts the device flow. The user enters a code on **https://github.com/login/device**, grants access, and then clicks **Star on GitHub** inside the application. The app requests `PUT https://api.github.com/user/starred/Gost1o1/YouTube-Music-Takeout-To-Lidarr` only for that final click. A 204 response confirms success.

### Security and privacy

- GitHub App user tokens are stored **in memory only** and discarded when the app exits. They are not included in exports, application settings or diagnostic logs. Reauthentication is needed after restart.
- The user can skip GitHub entirely and exit normally. The star action is never automatic, never occurs during authorization polling, and never performs an unstar.
- The app verifies the local web session token; endpoints bind only to 127.0.0.1 and accept same-origin requests. GitHub requests always go to fixed official HTTPS endpoints.
- GitHub can deny authorization or starring due to account settings, permissions, network errors, organization restrictions, expired code, or rate limits. The app must report the error rather than claim success.
- If no valid GitHub App Client ID is configured, starring is shown as unavailable and the rest of the app continues normally. There is intentionally no prepackaged private OAuth app or borrowed developer token.
- This is separate from **GitHub authentication in ChatGPT**. Connecting a GitHub app to ChatGPT does not configure an independently published Windows executable.

Official references: https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-user-access-token-for-a-github-app ; https://docs.github.com/en/rest/activity/starring
