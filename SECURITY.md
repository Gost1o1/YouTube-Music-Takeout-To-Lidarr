# Security notes — v0.9.0 preview

- This is development-only software; no independent security audit or Defender clearance has been completed.
- The local HTTP server listens on loopback, checks the request host/origin, and uses a random per-launch API session token. The Lidarr key remains in process memory and is never deliberately sent to outside metadata providers.
- Optional Apple/iTunes + Deezer research is NOT automatic: the user must press Start, which sends song and artist strings to those services. Their privacy policies/terms apply. The Takeout ZIP, YouTube URLs and Lidarr API key are not transmitted to them.
- Local audit report imports require matching Takeout row identities and never modify the original Takeout ZIP. They are not treated as freshly verified MusicBrainz matches.
- In this version all Lidarr API calls remain read-only. There is no artist creation, album searching, downloading or deletion.
- No PowerShell/VBScript launcher or antivirus bypass is bundled. Never disable security tools to run the app.
- Temporary diagnostic logging may include artist titles; do not publish private audit CSVs, music metadata, keys, logs or reports to GitHub.
