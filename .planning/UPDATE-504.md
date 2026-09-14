# Self-update prompt repair — build 504

## Report

A player installed the published 0.9.0 release, made the repository public and reached the main
menu with internet access. Although version 1.0.0 was available, no update prompt appeared.

## Evidence

The current `debug/Player.log` identifies the tested runtime as GloomhavenVR 0.9.0 / ModBuild 494
and records an `InvalidCastException` after main-menu arrival:

```
SelfUpdateDialog.BuildProgressRow -> Build -> ShowChoice -> SelfUpdateDriver.RunCheck
```

`SelfUpdateDriver.RunCheck` calls `ShowChoice` only after the public release check has completed,
parsed a release, found an update and confirmed that the main menu is available. The missing
prompt was therefore not caused by visibility, network access, release JSON or version ordering.

## Change

Every layout node built by `SelfUpdateDialog` now explicitly uses a `RectTransform`. In particular,
the Progress, Track and Fill nodes no longer create a plain `Transform` and then cast it to a
`RectTransform`.

The focused production harness opens the actual choice dialog, exercises progress rendering and
asserts that every affected node is a `RectTransform`. Its source mutation converts Progress back
to a plain `GameObject`; the harness must then fail with the logged `InvalidCastException`.

## Validation

The focused harness, strict Release build, refactor guard, mirrors and bilingual docs pass. The
headset retest used `install.ps1 -FakeVersion 0.9.0` and confirmed the visible update prompt.
The installer now combines the temporary version with `GhvrReleaseBuild=true`, so that test uses
the same enabled updater path as a published release instead of the deliberately disabled dev
build path. It does not alter the checkout or create a release ZIP.
