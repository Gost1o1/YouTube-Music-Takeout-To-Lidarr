# v0.9.0.7.1 — fix C# build error and album-review submission

## Error reported on Windows

`Program.cs(356,32): error CS0136: A local or parameter named 'body' cannot be declared in this scope because that name is used in an enclosing local scope`

## Root cause

In `HandleAsync`, the incoming POST body is already read into `body` and parsed as `json` at the top of the API handler. The new `approve-group` route declared another `body` inside the same method, which C# rejected. It also attempted to read the HTTP request input stream a second time, which would have failed even if the local were renamed.

## Fix

The endpoint now uses the previously parsed JSON, extracting the requested group ID through `Value("groupId")` and numeric album ID through `PropInt(json.RootElement, "albumId")`. It calls the unchanged `ApproveAlbumGroupSelection` routine. All operations remain session-only planning, **not Lidarr modifications**.

## Build and test on Windows

Run these commands **one at a time** from the extracted ZIP folder:

```powershell
dotnet build YouTubeMusicToLidarr.csproj -c Release -r win-x64
```

After a successful build:

```powershell
dotnet run --project YouTubeMusicToLidarr.csproj -c Release -r win-x64 --no-build -- --self-test
```

After passing tests:

```powershell
dotnet publish YouTubeMusicToLidarr.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Launch `publish\YouTubeMusicToLidarr.exe` and use your existing Takeout and audit CSVs; do **not** run fresh MusicBrainz verification just to test this hotfix. Run the read-only Lidarr comparison, open an album review group, and make one session-only selection. Verify that the plan changes while Lidarr's library does not.

## Validation limitations

The change was checked against the reported error and double-read path; ZIP integrity, manifest hashes, and browser JavaScript syntax were checked in the packaging environment. **A .NET SDK was not available, so this is not a compiler-tested build.**
