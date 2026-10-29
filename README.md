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
3. Double-click **`YouTubeMusicToLidarr.exe`**. Your browser opens the local app automatically. The development build may also show a console window; leave it open while using the app. Closing the final program tab now closes the EXE after a short grace period, once active writes finish.
4. Follow the on-screen steps to choose your YouTube Music Takeout, connect to Lidarr, and review your matches.
5. On **Finalize Lidarr Import**, select **Add to Lidarr Only** or **Add and Search**. Review the album selection and profiles before starting. The app tracks both requests sent and artists/albums verified back in Lidarr. It waits for the selected catalog items to be confirmed before showing batch results; any still-unverified source songs remain clearly outstanding.

The downloaded Windows build is self-contained; **you do not need PowerShell or the .NET SDK to run it**. No installation is required.

## What it does

- Reads your Google Takeout export and organizes artist/song information.
- Uses MusicBrainz and optional catalog evidence to suggest matches for review.
- Lets you review unclear results and save source metadata for later manual import.
- Compares planned additions with your existing Lidarr library before any changes.
- On your explicit approval, adds missing verified artists and monitors chosen, track-backed albums already known to Lidarr.
- Optionally queues Lidarr album searches, with progress and a downloadable results report.
- Saves a crash-safe import checkpoint to your Documents folder so a restarted app can inspect past actions and recheck Lidarr without automatically repeating album searches.

**What it cannot do yet:** Your YouTube Takeout is metadata, not audio files. The tool cannot upload or recreate songs from that metadata. Unverified and source-only manual entries stay in the app for later review or manual import. Two separate progress bars show **Lidarr requests checked** and **artists/albums confirmed in Lidarr**. Final completion means the selected catalog entries exist and albums are monitored—it does **not** mean the audio files are downloaded. Lidarr's searches and downloads may continue afterward.

**If you already ran an import using an older version:** Do **not** start the same searches again just to test this release. The checkpoint feature only records actions performed by v0.9.1.15.8 and later. Check your existing Lidarr albums and downloads first. A download-client error inside Lidarr is separate from this tool and will not be fixed by repeating an import.

**Privacy:** Your Takeout archive and Lidarr API key stay on your computer. Optional catalog lookups send relevant artist/song terms to their providers. See [Security & privacy](SECURITY.md).

The local import checkpoint contains album/artist names, identifiers and results (but no API keys). Protect it as you would your other music-library metadata.

## Need help?

- [Report a problem or suggest a feature](https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr/issues)
- [Source code and developer setup](YouTube-Music-Takeout-To-Lidarr/docs/developer/README.md)
- [Development history and older version notes](YouTube-Music-Takeout-To-Lidarr/docs/history/README.md)
- [Repository layout and updating instructions](YouTube-Music-Takeout-To-Lidarr/docs/developer/REPOSITORY_LAYOUT.md)
- [License](LICENSE) — personal and noncommercial terms; this is not an OSI open-source license.

Created by **Quinton Burgess (Gost1o1)** · [www.gost1o1.pro](https://www.gost1o1.pro)
