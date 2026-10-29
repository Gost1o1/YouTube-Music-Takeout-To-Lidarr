# YouTube Music Takeout → Lidarr

<p align="center">
  <img src="YouTube-Music-Takeout-To-Lidarr/AppUI/Gost1o1-logo.jpg" alt="Gost1o1" width="112" />
</p>

<p align="center">
  A Windows tool for reviewing your YouTube Music Takeout, identifying artists and songs, and sending verified additions to Lidarr.
</p>

<p align="center">
  <a href="https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr/releases"><img alt="Download for Windows" src="https://img.shields.io/badge/Download-Windows%20x64-086C4F?style=for-the-badge&logo=windows&logoColor=white" /></a>
  <a href="https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr/actions/workflows/build-windows.yml"><img alt="Development builds" src="https://img.shields.io/badge/Preview-Test%20Builds-325A82?style=for-the-badge&logo=githubactions&logoColor=white" /></a>
  <a href="https://www.gost1o1.pro"><img alt="Gost1o1 website" src="https://img.shields.io/badge/Visit-Gost1o1.pro-345C9C?style=for-the-badge&logo=googlechrome&logoColor=white" /></a>
</p>

<p align="center">
  <a href="https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr/actions/workflows/build-windows.yml"><img alt="Windows build status" src="https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr/actions/workflows/build-windows.yml/badge.svg" /></a>
</p>

> [!IMPORTANT]
> **Early development preview.** The final step can now add independently verified artists and monitor selected albums in Lidarr. You can optionally ask Lidarr to search those albums. **A search may download every track on an album, not just songs in your Takeout.** Source-only manual proposals remain local and are not automatically added. Please review your selections carefully.

## Download and start

1. Select **Download — Windows x64** above and download `YouTubeMusicToLidarr-win-x64.zip` from a published GitHub Release. **If no Release is listed yet**, choose **Preview — Test Builds**, open the newest successful run, and download its artifact (GitHub sign-in may be required).
2. Right-click the downloaded ZIP → **Extract All**. Keep the `AppUI` folder beside `YouTubeMusicToLidarr.exe`.
3. Double-click **`YouTubeMusicToLidarr.exe`**. Your browser opens the local app automatically. The development build may also show a console window; leave it open while using the app.
4. Follow the on-screen steps to choose your YouTube Music Takeout, connect to Lidarr, and review your matches.
5. On **Finalize Lidarr Import**, select **Add to Lidarr Only** or **Add and Search**. Approve the albums and profiles shown before starting an import. The app displays both the requests it sends and Lidarr's separate background activity.

The downloaded Windows build is self-contained; **you do not need PowerShell or the .NET SDK to run it**. No installation is required.

## Watch the walkthrough

Want to see the process before starting? Watch the step-by-step walkthrough by **Gost1o1**:

<p align="center">
  <a href="https://www.youtube.com/watch?v=n81hzCCk8M0" title="Watch the YouTube Music Takeout to Lidarr walkthrough on YouTube">
    <img src="https://img.youtube.com/vi/n81hzCCk8M0/hqdefault.jpg" alt="Watch Gost1o1's YouTube Music Takeout to Lidarr walkthrough" width="560" />
  </a>
</p>

<p align="center"><a href="https://www.youtube.com/watch?v=n81hzCCk8M0"><strong>▶ Watch the setup and usage video on YouTube</strong></a></p>

## What it does

- Reads your Google Takeout export and organizes artist/song information.
- Uses MusicBrainz and optional catalog evidence to suggest matches for review.
- Lets you review unclear results and save source metadata for later manual import.
- Compares planned additions with your existing Lidarr library before any changes.
- On your explicit approval, adds missing verified artists and monitors chosen, track-backed albums already known to Lidarr.
- Optionally queues Lidarr album searches, with progress and a downloadable results report.

**What it cannot do yet:** Your YouTube Takeout is metadata, not audio files. The tool cannot upload or recreate songs from that metadata. Unverified and source-only manual entries stay in the app for later review or manual import. The progress bar reports completed Lidarr API actions; Lidarr's metadata refreshes, searches, downloads and file imports may continue afterward.

**Privacy:** Your Takeout archive and Lidarr API key stay on your computer. Optional catalog lookups send relevant artist/song terms to their providers. See [Security & privacy](SECURITY.md).

## Need help?

- [Report a problem or suggest a feature](https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr/issues)
- [Source code and developer setup](YouTube-Music-Takeout-To-Lidarr/docs/developer/README.md)
- [Development history and older version notes](YouTube-Music-Takeout-To-Lidarr/docs/history/README.md)
- [Repository layout and updating instructions](YouTube-Music-Takeout-To-Lidarr/docs/developer/REPOSITORY_LAYOUT.md)
- [License](LICENSE) — personal and noncommercial terms; this is not an OSI open-source license.

Created by **Quinton Burgess (Gost1o1)** · [www.gost1o1.pro](https://www.gost1o1.pro)
