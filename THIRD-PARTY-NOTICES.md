# Attribution and third-party components

## Grimlite-Li

This project derives from https://github.com/jib-akbr/Grimlite-Li at commit
`aa0aa2a169c40f14d9cb6e38f1b94da4288a52ba`, itself derived from
https://github.com/Froztt13/Grimlite-Li.

The upstream attribution states:

> Copyright: © 2025–2026 jib-akbr (original modifications and contributions only)

The existing source notices are retained. The GPL-3.0 license text is in LICENSE,
and the upstream README is retained in docs/upstream-readme.md. This import adds
the experimental native Ruffle bridge, loader compatibility adapter, runtime
selection and recovery controls, smoke tests, player acquisition helper, and
documentation. The source-import and dependency-staging changes are dated
2026-11-19. The original authors retain their respective rights.

## Bonxie reference

https://github.com/retr0gr4d3/Bonxie informed the native hosting and `skuaCall`
bridge protocol used here. Its repository is not vendored. Its README describes
MIT licensing alongside an "Educational & Personal Use Only" label; that
ambiguity must be clarified when relying on a license grant for adapted code.
This project's GPL notice does not resolve or replace any applicable upstream
license requirements.

## Mugetsu and Ruffle

The player helper retrieves Skua Mugetsu `v1.6.0-skua.5` from:
https://github.com/retr0gr4d3/skua-mugetsu/releases/tag/v1.6.0-skua.5

Player source and build patches: https://github.com/retr0gr4d3/skua-mugetsu

Mugetsu's changes are GPL-3.0. Its Ruffle and wgpu components retain their
MIT/Apache-2.0 notices; gc-arena retains its MIT/CC0 notices. The downloaded
player includes LICENSES and THIRD-PARTY-NOTICES.txt for its libraries and fonts.
The helper preserves them. The native executable is not checked into this repo.
If distributing it, retain its complete notices and arrange access to the exact
corresponding source and build materials required by its license.

## Legacy loader, libraries, and resources

The original compiled loader and legacy DLL references are not checked in.
tools/legacy-dependencies.json records the upstream commit and SHA-256 hashes
used for local builds. tools/Get-LegacyDependencies.ps1 can retrieve those files
from upstream or verify and copy them from an existing checkout. Optional
compiled plugins and bot collections are not included.

The inspected upstream tree did not contain the loader's original ActionScript
source. Source availability and redistribution rights for the loader and legacy
libraries must be established before a binary release; retrieving them from a
public repository does not establish those rights. UI source resources retained
from Grimlite keep their existing attribution; this import is not a complete
audit of the ownership of each resource.

Managed packages listed in packages.config are restored through NuGet. Their
own license terms and notices continue to apply. AQW and its game assets belong
to their respective owners; no license to redistribute the official game is
granted here.
