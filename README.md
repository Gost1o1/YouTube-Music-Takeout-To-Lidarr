# YouTube Music Takeout → Lidarr

<p align="center">
  <img src="AppUI/Gost1o1-logo.jpg" alt="Gost1o1" width="112" />
</p>

<p align="center">
  A Windows tool for reviewing your YouTube Music Takeout, identifying artists and songs, and preparing a Lidarr migration.
</p>

<p align="center">
  <a href="https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr/releases"><img alt="Download for Windows" src="https://img.shields.io/badge/Download-Windows%20x64-086C4F?style=for-the-badge&logo=windows&logoColor=white" /></a>
  <a href="https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr/actions/workflows/build-windows.yml"><img alt="Development builds" src="https://img.shields.io/badge/Preview-Test%20Builds-325A82?style=for-the-badge&logo=githubactions&logoColor=white" /></a>
</p>

<p align="center">
  <a href="https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr/actions/workflows/build-windows.yml"><img alt="Windows build status" src="https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr/actions/workflows/build-windows.yml/badge.svg" /></a>
</p>

> [!IMPORTANT]
> **Early development preview.** The current application prepares matching evidence, local manual-import metadata, and a read-only Lidarr preview. **It does not download music or actually add songs/albums to Lidarr.** Source-only saves are not verified MusicBrainz matches.

## Download and start

1. Select **Download — Windows x64** above and download `YouTubeMusicToLidarr-win-x64.zip` from a published GitHub Release. **If no Release is listed yet**, choose **Preview — Test Builds**, open the newest successful run, and download its artifact (GitHub sign-in may be required).
2. Right-click the downloaded ZIP → **Extract All**. Keep the `AppUI` folder beside `YouTubeMusicToLidarr.exe`.
3. Double-click **`YouTubeMusicToLidarr.exe`**. Your browser opens the local app automatically. The development build may also show a console window; leave it open while using the app.
4. Follow the on-screen steps to choose your YouTube Music Takeout and connect to your local Lidarr instance.

The downloaded Windows build is self-contained; **you do not need PowerShell or the .NET SDK to run it**. No installation is required.

## What it does

- Reads your Google Takeout export and organizes artist/song information.
- Uses MusicBrainz and optional catalog evidence to suggest matches for review.
- Lets you review unclear results and save source metadata for later manual import.
- Compares planned additions with your existing Lidarr library without changing it.

**Privacy:** Your Takeout archive and Lidarr API key stay on your computer. Optional catalog lookups send relevant artist/song terms to their providers. See [Security & privacy](SECURITY.md).

## Need help?

- [Report a problem or suggest a feature](https://github.com/Gost1o1/YouTube-Music-Takeout-To-Lidarr/issues)
- [Developer setup and testing](docs/developer/README.md)
- [Technical history and older version notes](docs/history/README.md)
- [License](LICENSE) — personal and noncommercial terms; this is not an OSI open-source license.

Created by **Quinton Burgess (Gost1o1)**.
