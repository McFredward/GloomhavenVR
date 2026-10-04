# Wireless Quest hardware installation

`scripts/install-quest-wireless.cmd` is the Windows double-click entry point.
It connects the remembered Quest over Wi-Fi, installs the selected local APK
with `adb install -r`, and starts the app. The accompanying PowerShell and Python
entry points support command-line use. The installer selects the latest verified private diagnostic; the current
hardware checkpoint is618 and remains distinct from a playable campaign.

## First installation on Windows

1. Extract the complete checkout or private test package into a writable folder.
   Python and ADB require no manual installation: the starter prepares its private
   Python runtime/venv, and missing ADB is downloaded locally as Google's official
   [SDK Platform-Tools](https://developer.android.com/tools/releases/platform-tools).
   Existing ADB installations can be reused; an explicit path is accepted below.
2. Put the PC and Quest on the same Wi-Fi network. Enable the headset's existing
   developer mode, connect it with a USB data cable and allow USB debugging in
   the headset. Keep the headset awake while installing.
3. Supply the local APK source once, using a PowerShell terminal at the repository
   root. For a private diagnostic handoff, keep its APK next to `handoff.json`:

   ```powershell
   .\scripts\install-quest-wireless.cmd -Handoff "D:\Quest tests\handoff.json"
   ```

   For builds made by the local converter:

   ```powershell
   .\scripts\install-quest-wireless.cmd -OutputRoot "D:\Quest builds"
   ```

   When the handoff already lives in `.planning/debug/quest3/`, or the builder
   uses `.planning/quest3-local/`, just double-click the `.cmd` file.
4. The script discovers the Quest's WLAN address over USB, enables ADB TCP/IP on
   port 5555 and verifies the same headset over Wi-Fi before installing. Once
   successful, disconnect the USB cable.

The process uses Meta's documented
[USB-to-Wi-Fi ADB procedure](https://developers.meta.com/vr/documentation/native/android/ts-adb/).
No registered Meta application or app-level Horizon service is needed by this
installer. Headset developer mode and ADB authorization are still required.

## Managed Python environment

The Windows starter creates `scripts/.quest-venv/` and uses that environment's
interpreter directly. Activation and execution-policy changes are unnecessary.
Its base runtime lives in `scripts/.quest-python/`: the pinned CPython3.14.8
Windows x64 or ARM64 distribution is downloaded from CPython's official NuGet
package and checked against its pinned SHA-256 before extraction. This is the
[Python team's documented standalone build distribution](https://docs.python.org/3/using/windows.html#the-nuget-org-packages).
It does not install a global Python, alter PATH or require administrator rights.

The first provision needs Internet access for the approximately 15 MB runtime;
successful later runs reuse the local cache. The installer currently uses only
the Python standard library. `tools/quest-installer/requirements.txt` therefore
declares no third-party dependencies. If future versions add pinned dependencies,
the bootstrap installs them into this venv and records the requirements hash
only after success. It refreshes dependencies when that manifest changes.
There is no routine pip upgrade or empty-package download.

The bootstrap checks the environment's location and base interpreter before
reuse, so moving a checkout or extracting a new test folder cannot silently use
an environment bound to the old path. Repair affects only folders it owns;
unrecognized existing folders are retained. Both managed directories are ignored
by Git. `-DryRun` can initialize these Python files first; APK validation then
runs without contacting ADB or writing Quest connection settings.

## Managed ADB on Windows

If no explicit, remembered, PATH, Android SDK or SideQuest ADB is available,
the installer downloads the pinned Windows Platform-Tools37.0.1 archive from
Google into `scripts/.quest-adb/`. The approximately 8 MB archive is checked
against its exact size and SHA-256 before extraction. The official tools and
their license files stay inside the managed folder; PATH, Android Studio and
system-wide tools are not changed. Administrator rights are unnecessary.

Later runs reuse this cache, including its companion DLLs, with integrity checks
before use. Only owned cache content can be repaired. A failed setup stops before
any headset command or installation settings write; rerun after fixing the
connection. `-DryRun` does not download or provision ADB. An explicit `-Adb` path
is respected, including a clear error if that supplied executable is missing.

## Each following hardware test

Double-click `scripts/install-quest-wireless.cmd`. Leave the newest completed
builder output or updated private handoff at the remembered location. The
installer reconnects, validates the current artifact and updates the app.

Merging successive archives into the same Windows folder is supported. Automatic
selection compares build stamps embedded in the APKs, so remembered paths and
older remaining APKs do not pin a test to an earlier build. Incomplete obsolete
candidates are reported and skipped. Explicit `-Apk`, `-Handoff` or `-OutputRoot`
selects that exact source and keeps strict verification. B618 and later confirm
the actual Android build and input stamp before launch; both are retained in the
installation receipt and independently queried during log collection.

The builder's `latest-build.json` identifies its latest successful build; its
receipt and APK hashes are checked. A handoff identifies one APK and its hash.
Incomplete builds and archived APKs cannot win selection merely because their
file modification times are newer. The installer does not download APKs or
publish original game content.

For an explicitly selected local APK without a receipt:

```powershell
.\scripts\install-quest-wireless.cmd -Apk "D:\Quest tests\GloomhavenVR-Quest.apk"
```

This manual source reuses that exact path on subsequent runs. It has no builder
provenance; prefer the builder output or handoff for automatic test updates.

The ignored `.planning/debug/quest3/wireless-install.json` remembers the successful
APK source, ADB executable, WLAN endpoint and hardware identity. Per-run logs and
installation evidence stay private beside the configuration. They are not app
save files and contain no signing key or account credentials.

## Connection changes and options

After a headset reboot, wireless ADB may need to be enabled again. Connect USB,
allow debugging if prompted, and rerun the script. `-Setup` explicitly refreshes
the USB-derived WLAN address. A router DHCP reservation can keep that address
stable between tests. A reachable saved address is checked against the remembered
headset identity before any APK installation.

```powershell
# Refresh the WLAN connection using the attached, authorized Quest.
.\scripts\install-quest-wireless.cmd -Setup

# Use an already enabled Wi-Fi endpoint, or override a changed address.
.\scripts\install-quest-wireless.cmd -QuestHost "192.168.1.70"

# Select the USB device when more than one Quest is attached.
.\scripts\install-quest-wireless.cmd -Setup -Serial "YOUR_QUEST_USB_SERIAL"

# Use a particular ADB executable; it is remembered after successful installation.
.\scripts\install-quest-wireless.cmd -Adb "D:\Android\platform-tools\adb.exe"

# Validate APK selection without contacting any device or changing configuration.
.\scripts\install-quest-wireless.cmd -DryRun

# Install without launching the app.
.\scripts\install-quest-wireless.cmd -NoLaunch
```

The `.cmd` window stays open so installation errors remain readable. For scripted
use without its final pause, invoke the `.ps1` directly. Linux and macOS use the
same core with Python and lower-case CLI options:

```sh
python3 scripts/install-quest-wireless.py --handoff /path/to/handoff.json
python3 scripts/install-quest-wireless.py
```

If ADB reports `unauthorized`, accept the debugging prompt in the headset.
If reconnecting fails, check that the Quest is awake, USB authorization is valid,
and the Wi-Fi network allows direct communication between its clients. An
unreachable saved address with no authorized USB Quest produces recovery
instructions. Multiple candidate headsets require an explicit selection.

Updates preserve local app data under the same package and signing key. Signature
conflicts stop installation and report the error; the script never uninstalls,
clears app data or forces a downgrade to work around them. The existing builder's
signing files must remain intact for future updates.

## Validation limits

The portable installer suite covers connection setup/reconnect, device selection,
artifact validation and failed installation paths using controlled ADB responses.
PowerShell wrapper parsing/argument forwarding can be verified on a portable
PowerShell runtime. The bootstrap suite also exercises actual isolated venvs,
cache reuse, moved/broken environments, ownership and locking, pinned archive
validation and a local hash-pinned dependency install with a hash-mismatch control.
Legacy native argument mode is exercised explicitly: it reproduced the
maintainer's first Windows5.1 quote-loss error, and the corrected interpreter
probe and complete setup/reuse pass in that mode. The maintainer's next Windows
run confirms actual CPython download, venv creation and B609 APK selection.
Run it with `GHVR_PWSH_PATH` pointing to `pwsh`, or put `pwsh` on PATH; without
PowerShell these controls are explicitly skipped. Managed Windows ADB and the
headset transport still need the Windows hardware run.
These checks do not establish an actual wireless connection
to the maintainer's Quest; that requires the headset test.

See [the diagnostic hardware procedure](QUEST3-HARDWARE-609.md) for controls,
evidence capture and the outstanding full-game conversion gates.

## Collecting a standalone run on Windows

After a test, keep the headset awake and double-click
`scripts/collect-quest-logs.cmd` (or `Collect-Quest-Logs.cmd` in the private package).
It reuses the local Python/venv, managed ADB and remembered verified Wi-Fi endpoint.
If Wi-Fi cannot reconnect, attach an authorized USB Quest: collection can read USB
directly without enabling TCP/IP. An APK or Unity installation is unnecessary.

Each run writes a private timestamped ZIP under
`.planning/debug/quest3/captures/`. It includes recent main/crash logcat, the
allowlisted diagnostic app log/previous log, storage marker and hardware snapshot,
plus firmware/package/provenance information and explicit missing-file errors.
Historical build banners are kept distinguishable from actual installed-package
queries and local PC receipts. Scoped-storage restrictions can leave gaps; the
script retains useful partial captures. Collection never clears logs, restarts or
installs the app, deletes saves or uploads evidence.

```powershell
# Collect directly from authorized USB, without changing Wi-Fi settings.
.\scripts\collect-quest-logs.cmd -Setup

# Select a headset or a different PC destination.
.\scripts\collect-quest-logs.cmd -Serial "YOUR_QUEST_SERIAL" -OutputRoot "D:\Quest captures"

# Use an already enabled wireless endpoint.
.\scripts\collect-quest-logs.cmd -QuestHost "192.168.1.70"
```

Copy the resulting ZIP and relevant pictures into the main checkout's ignored
`.planning/debug/quest3_probleme/` and describe the triggering action. The files
can include baked profile IDs, headset identifiers and recent Android log lines;
keep them private. `-OutputRoot` here is the capture destination, whereas the
installer's same option denotes a builder-output source.
