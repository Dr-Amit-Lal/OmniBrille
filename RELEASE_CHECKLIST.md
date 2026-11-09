# Spatial workspace release evidence checklist

Current target: `1.2.0-nebula.6`. Unchecked items are requirements to record, not claims of completed tests. Historical release/run reports retain their original scope.

- [ ] Record exact source commit, clean checkout, version metadata and release artifact identity.
- [ ] Run documentation links/fences, formatting, analyzer-enabled Release build, ordinary/headless tests and dependency audit.
- [ ] Verify local-only boundaries: explicit roots/pins, migration, transient search previews, stale-work rejection, bounded scenes, root confinement and file-opening policy.
- [ ] Independently review saved-data behavior, installer cleanup, release notes and limitations.
- [ ] Build the exact Windows x64 installer; preserve MIT/component notices and reviewed DNG-free native hashes.
- [ ] Reject Voice/models, NAudio, OmniSorSe binaries and prohibited developer/source/test/private artifacts.
- [ ] Match checksum, manifest schema 6/local-workspace identity, version, source commit, signature state and generated notes.
- [ ] Run hosted install, known-retired-file fixture cleanup, unknown-file/settings preservation, launcher/registration/version, normal close/relaunch and uninstall against those exact bytes.
- [ ] Record full predecessor upgrade separately; preserve saved roots/pins/preferences and user content.
- [ ] Record native multi-root/pin/Search/hover/refresh, unavailable drive and cloud-placeholder behavior with synthetic data.
- [ ] Record dense/narrow rendering across full-motion frames, bounded hover/neighbor response, local ring/label/connector clearance, scene-change transitions, DPI/GPU, Reduced motion/effects, real UIA/Narrator/NVDA and window controls.
- [ ] Disclose unsigned/Unknown Publisher/SmartScreen status and all unperformed manual checks.
- [ ] Retain the matching installer, sidecar, manifest, dependency inventory, generated notes and validation record; do not substitute rebuilt bytes after validation.

Do not publish, merge or mark an unchecked gate passed merely because its workflow exists. [Testing](docs/testing.md) and [packaging](docs/PACKAGING.md) describe evidence boundaries.
