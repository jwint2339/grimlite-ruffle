# Grimlite Ruffle

An experimental native Ruffle integration for [Grimlite-Li](https://github.com/jib-akbr/Grimlite-Li).
The existing Windows application and bot API can communicate with the Skua build of
Mugetsu, a modified Ruffle player, instead of Adobe Flash ActiveX.

This is an initial source import. There is no packaged release yet. Basic game
reads, packet events, embedding, and clean shutdown have been tested; combat,
quests, reconnects, legacy plugins, and long sessions still need validation.

## Build on Windows

Requirements: Visual Studio 2022 or its Build Tools with .NET desktop development,
the .NET Framework 4.7.2 targeting pack, NuGet CLI, and PowerShell. The pinned
Mugetsu player requires Windows x64 and suitable graphics support. Adobe Flash
ActiveX is needed only when selecting the legacy Flash runtime.

From the repository root:

```powershell
# Obtain the original loader and legacy build references from a pinned upstream commit.
.\tools\Get-LegacyDependencies.ps1

# Restore managed packages, fetch the pinned native player, and build.
nuget restore "Project Grimlite.sln"
.\build.ps1 -Ruffle

# Run the Ruffle runtime for this launch.
.\bin\Release\Grimoire.exe --ruffle
```

Alternatively, `Get-LegacyDependencies.ps1 -SourceDirectory 'path\to\Grimlite-Li'`
copies the same verified dependencies from an existing upstream checkout.
Downloaded dependencies, NuGet packages, player files, build output, profiles,
and logs are ignored by Git. Optional upstream bot collections and plugins are
not bundled in this source import.

Flash remains the default for configurations without a runtime preference.
Use `--ruffle`, or enable **Options > Use Ruffle (next launch)** and restart.
Keep the generated `Player` directory beside the built executable.

See [runtime documentation](docs/ruffle.md) for architecture, tests, and known
limitations. The [upstream README](docs/upstream-readme.md) preserves the original
usage notes and attribution.

## License and credits

This derivative is distributed under [GNU GPL version 3](LICENSE). Preserve the
existing notices and identify modifications when redistributing it.

- Original Grimlite-Li: [Froztt13](https://github.com/Froztt13/Grimlite-Li).
- Upstream fork used here: [jib-akbr/Grimlite-Li](https://github.com/jib-akbr/Grimlite-Li),
  commit `aa0aa2a169c40f14d9cb6e38f1b94da4288a52ba`.
- Ruffle integration and source-import changes: jwint2339, 2026-11-19.
- Native bridge/player integration reference: [Bonxie](https://github.com/retr0gr4d3/Bonxie).
- Player: [Skua Mugetsu](https://github.com/retr0gr4d3/skua-mugetsu), based on
  [Mugetsu](https://github.com/mugetsu-aqw/mugetsu) and [Ruffle](https://ruffle.rs/).

See [third-party and source notes](THIRD-PARTY-NOTICES.md). The GPL license for
this project does not grant rights to independently owned game assets or other
third-party components. Dependency download instructions are not a clearance
to redistribute those components. Binary releases require a separate review of
their notices and matching source availability.
