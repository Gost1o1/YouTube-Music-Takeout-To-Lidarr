# v0.9.0.8 build and test report

**Native .NET build: NOT RUN.** The authoring environment lacks `dotnet`, `csc`, `mcs`, and `msbuild`; a network attempt to obtain the SDK failed DNS resolution. No `.exe` was produced here. The existing Windows GitHub Actions workflow can compile, self-test, publish, and upload an executable once the repository source is pushed; it has not been triggered by this package.

**Checks run:**

- Python offline replay of 2,505 user-supplied CSV records: 609 baseline manual, 349 projected manual, 187 recording-MBID-disputed album-search candidates, 73 edition-title candidates, 260 new planning candidates.
- JavaScript parse using Node.js `new Function` on the embedded application script: passed.
- C# token/bracket structural scan for all `.cs` files using Pygments CSharpLexer: passed (not a substitute for Roslyn compilation).
- Synthetic negative test replay: unrelated album, missing candidates, and file-backed disagreement remain manual; edition aliases remain approval-gated.
- ZIP CRC validation and SHA-256 checksums are verified at packaging.

**Windows verification required:** `dotnet build ...`, `dotnet run ... -- --self-test`, `dotnet publish ...` and then compare against the same Lidarr library and latest audit CSVs. No Lidarr writes or downloads are implemented in this release.
