# SoundDial working rules

Read `PROJECT_HANDOFF.md` and the root `README.md` before changing anything.

## Scope and ownership

- SoundDial is Simracingshark's hardware project. The enclosure, CAD, printing,
  electronics integration, assembly and physical testing are his work.
- The firmware and companion applications were developed with AI assistance and
  tested and tuned on the real device. Preserve that wording in public material;
  do not exaggerate either the AI contribution or the project claims.
- Talk to the owner in straightforward, informal Russian. Keep explanations
  practical and do not pad them with praise.

## Safety rules

- Never publish, push, create a release, upload to MakerWorld, delete old user
  material or rewrite Git history unless the owner explicitly asks in that turn.
- Treat the current working tree as valuable. There may be intentional
  uncommitted changes. Inspect `git status` before editing and preserve unrelated
  work.
- Do not change the ESP32 wire protocol or require a firmware reflash merely to
  support another desktop OS. Windows and Linux clients must use the same
  firmware unless the owner explicitly approves a protocol change.
- Do not silently change pin assignments, control gestures, configuration names
  or defaults. They are already represented in hardware, manuals and releases.
- Do not replace real product photos with generated versions. Image generation
  may be used for decorative icons only when requested.
- Do not claim hardware or desktop integration was tested when it was only
  compiled or covered by self-tests. State the boundary clearly.

## Implementation rules

- Make the smallest change that fixes the requested behavior. The project has
  previously regressed encoder responsiveness, seek behavior and desktop-client
  stability after broad edits.
- Keep rotation handling non-blocking. Coalesce rapid volume changes instead of
  queueing every encoder tick, while ensuring the final requested value is sent.
- Configuration is edited while the application is running. A partially saved
  TOML file must not crash or freeze the client; retain the last valid config and
  retry after the save completes.
- Keep battery measurement optional. A wired USB-only build must work without a
  battery, LX-LCBST, latching switch or GPIO0 divider.
- Preserve battery readings on the dial without a connected PC. The ESP32 owns
  measurement and persistent thresholds; the PC clients only configure them.
- On Linux, use ordinary PipeWire/PulseAudio and MPRIS targets. Voicemeeter and
  SteelSeries Sonar are Windows-only and must not be presented as Linux support.
- Reuse shared protocol/configuration code where practical, but do not introduce
  Windows-only APIs into the Linux build.

## Required verification

- Run the relevant self-tests after desktop-client changes.
- Build the affected project after source changes.
- For firmware changes, build both `esp32-c3-supermini` and `hardware-test` when
  the toolchain is available.
- Check `git diff` and report exactly what was and was not verified.
- A successful compile is not a substitute for testing against the physical
  SoundDial, PipeWire, MPRIS, Firefox/Chromium or Spotify.

## Release discipline

- Keep development source, generated binaries and public release packages
  distinct.
- Before any release, verify the README, manuals, example config, checksums and
  archive contents against the actual build.
- Do not put raw `.exe`, PlatformIO project files or unrelated source archives on
  MakerWorld. MakerWorld receives printable files, selected manuals, photos and
  links to GitHub; software belongs in GitHub Releases.
- The Windows executable is not code-signed. Keep the existing transparent
  warning; never describe it as signed or guaranteed safe by antivirus vendors.

