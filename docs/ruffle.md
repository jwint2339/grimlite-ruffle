# Native Ruffle engine (experimental)

Grimlite can host Bonxie's pinned Mugetsu native player while retaining its
existing bot API and GBot serialization. Flash remains the default when no
runtime preference exists.

## Build and use

1. Run `tools/Get-LegacyDependencies.ps1` to obtain the original loader and DLL
   references (or use `-SourceDirectory` with an existing Grimlite-Li checkout).
   These compiled dependencies are deliberately excluded from this source import.
   Restore `Project Grimlite.sln` with NuGet as usual.
2. Run `tools/Get-RufflePlayer.ps1`. This downloads Mugetsu 1.6.0-skua.5,
   verifies the archive SHA-256, and prepares the ignored `Player` directory.
   Its license and notice files are preserved. `-ArchivePath` accepts an
   already-downloaded copy of the same pinned archive.
3. Build the Release configuration using full .NET Framework MSBuild and the
   .NET Framework 4.7.2 targeting pack. `build.ps1 -Ruffle` combines player
   preparation with the normal build. Optional `-MSBuildPath` and
   `-ReferenceAssembliesPath` support a project-local toolchain.
4. In Grimlite's Options menu, check **Use Ruffle (next launch)** and restart.
   Alternatively, start `Grimoire.exe --ruffle` for a one-off developer test.

The `Player` folder must stay beside the executable. Missing files or an
unsupported loader show recovery controls in the game area; no running bot
is silently moved to another engine. **Use Flash** recovers the existing path.
The Ruffle profile/cache and generated loader are stored under `RuffleData`
beside the executable. The pinned player is a separate Windows x64 process;
use the same hardware-supported build validated by Bonxie on the target PC.

## Architecture

- `NativeGamePanel` supplies a stable Win32 parent handle and resizes its child.
- `NativeRuffleBridge` owns the player process and a loopback TCP connection.
  Requests are newline-delimited JSON with IDs; a background reader completes
  pending calls independently of the UI thread. Calls have a five-second bound.
- Outgoing game notifications enter an ordered, bounded queue drained on the
  WinForms thread. Handlers can call ActionScript again without re-entering
  the runtime's original ExternalInterface call stack.
- The two existing `Flash.Call` overload families keep their different return
  contracts: raw loader JSON for object-array/string calls, versus typed JSON
  deserialization for string-array calls. The existing assembly identity and
  serialized command types are unchanged.
- Shutdown cancels bot work, closes the bridge socket, waits for the player,
  and only then disposes the native host. Forced termination is a bounded
  recovery for an unresponsive child, not the normal exit path.

## Loader compatibility shim

The native player recognizes `skuaCall(eventName, argumentArray)`. The bundled
Grimlite loader instead calls `ExternalInterface.call(eventName, ...args)`.
Rather than maintaining a Rust fork, `RuffleLoader` generates a temporary
compatibility copy of the pinned upstream SWF at startup. It changes only
`grimoire:Externalizer/call` to use that wrapper. The original SWF stays intact.

This deliberately supports only source SHA-256
`BB3CF5ABAA939B96F62B57CAFAACF03E70267B4976F099A6525EF88D1A06120E`.
For this inspected file, the ABC payload has 1066 strings and method 134 has
one named argument plus NEED_REST. The adapter appends string `skuaCall`, adds
one pushstring instruction, increments the call's argument count and max stack,
and updates the ABC tag and SWF lengths. It emits uncompressed FWS. The method
has no branches or exception ranges to relocate. Hash and bytecode checks
reject other loaders rather than modifying them speculatively.

The event protocol is one-way. The retained legacy `modifyServers` return-value
path is not supported by Ruffle; the bundled loader uses `getServers` and
`getServers2`, which work as notifications. A future loader requiring immediate
host return values needs an explicit bridge extension.

## Compatibility limits

- Legacy plugins that directly access `Root.flashPlayer` or `Flash.flash` need
  the Flash engine. Those fields are null under Ruffle; the existing delegates
  retain their signatures for plugins that use the facade and ignore the control.
- The known failing cosmetic `SetTitle` call is skipped on Ruffle. The native
  game panel still displays the game's normal title/artwork.
- The optional local `Loader/bg.jpg` is absent in this repository. Its load
  error does not stop the official game scene from rendering.
- Basic reads and event transport do not establish full bot parity. Combat,
  quests, reconnects, plugins, and long-running workloads require live checks.
- Closing a standalone player window was observed to exit abnormally; the
  embedded engine uses the verified host-connection-first shutdown path.

## Validation

`tests/RuntimeSmokeTests.csproj` checks loader integrity/rejection and legacy
return conversions, then exercises the real native player: embedding, game
reads, progress/debug delivery, a callback from an incoming event, and clean
exit without forced termination. It accepts repository and scratch-data paths.

`tests/ApplicationSmokeTests.csproj` runs the real application startup and
public bot-facing facade. Stage it beside the normal build output and start it
with `--ruffle`; it closes automatically after pre-login checks. `--live` waits
for manual login, checks public Player/World/Inventory reads, verifies packet
events and the server list, and closes through the actual application's shutdown
handler. It never starts a bot or issues movement/combat/quest/item commands.
Reports contain counts and status only, not credentials or packet payloads.

Validated locally on 2026-11-19:

- Release build: 0 errors, 55 pre-existing warnings.
- Native embedding, event-to-callback reentry, loader integrity, and return
  conversion smoke checks passed.
- Actual Grimlite/Ruffle live check passed after manual login: three samples
  of health/mana, 8 inventory items, 4 cells, and a valid empty monster list.
  The application populated 26 servers and received client packets, server
  packets, connection notifications, and extension (`pext`) events.
- The actual application's close handler produced player exit code 0 without
  forced termination.
- Original Flash startup and its existing string-array callback API passed
  a separate pre-login regression check. Full Flash bot parity was not retested.

The live game still emitted ActionScript null-reference errors during map/NPC
initialization, as it did in the earlier isolated player check. These did not
prevent the sampled C# reads or event delivery. A populated monster list,
combat/actions, quests, reconnects, legacy plugins, and long sessions remain
outside this validation.

## Player source and notices

- Bonxie bridge: <https://github.com/retr0gr4d3/Bonxie>
- Mugetsu Skua build: <https://github.com/retr0gr4d3/skua-mugetsu>
- Pinned release: <https://github.com/retr0gr4d3/skua-mugetsu/releases/tag/v1.6.0-skua.5>

Mugetsu's changes are GPL-3.0 and its upstream Ruffle components retain their
own notices. Distribute the complete staged Player directory, including
LICENSES and THIRD-PARTY-NOTICES.txt.
