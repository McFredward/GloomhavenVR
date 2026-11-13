"""Remember a verified Quest Wi-Fi endpoint after completed USB work.

The maintainer's 2026-10-16 capture followed a successful USB installation, but
subsequent log collection still required the cable: remembering a USB serial
does not enable or remember wireless ADB. Meta's documented workflow is to obtain
the headset's Wi-Fi address, enable ``adb tcpip 5555`` over USB and connect to
that address. Enabling TCP mode can restart adbd, so this helper belongs AFTER
the installation's transfers, recovery, launch and cleanup, never between them.

This is a bounded convenience, not an installation requirement. Reuse an
already authorized endpoint first; restart adbd only when no listener is known.
Never scan a network, change the caller's active transport, adopt a different
headset, or report an unverified IP as ready. A saved hint is not a promise that
the listener or DHCP address will survive a headset restart/network change.

https://developers.meta.com/vr/documentation/native/android/ts-adb/
https://developer.android.com/tools/adb#wireless
"""
from __future__ import annotations

import ipaddress
import re
import subprocess
import time


class _IdentityChanged(RuntimeError):
    pass


def _ipv4_endpoint(value):
    """Accept an observed address, not hostnames or arbitrary discovery strings."""
    if not isinstance(value, str): return None
    match = re.fullmatch(r"(\d+\.\d+\.\d+\.\d+):(\d{1,5})", value)
    if not match or not 1 <= int(match[2]) <= 65535: return None
    try: address = ipaddress.IPv4Address(match[1])
    except ipaddress.AddressValueError: return None
    if address.is_loopback or address.is_unspecified or address.is_multicast or int(address) == 0xffffffff:
        return None
    return str(address) + ":" + str(int(match[2]))


def remember_after_usb(adb, serial, hardware, config, *, enable=True, budget_seconds=12):
    """Return ``(settings, result)``; ordinary wireless failure retains settings.

    ``enable=False`` is for the independent Connect action: record an already
    authorized Wi-Fi transport without restarting adbd or making a new network
    connection. The installer may enable TCP mode only after its completed work.
    All ADB commands share a hard twelve-second budget and disable reconnect
    waits. Cancellation remains cancellation, including the Wizard runner's
    cross-module ``Cancelled`` exception.
    """
    started = time.monotonic()
    settings = dict(config)
    deadline = started + min(12, max(0, float(budget_seconds)))
    attempted = False
    result = {"status": "unavailable", "hardwareSerial": hardware, "setupAttempted": False}

    def finished(status, *, endpoint=None, reason=None, detail=None):
        result.update(status=status, setupAttempted=attempted,
                      elapsedSeconds=round(time.monotonic() - started, 3))
        if endpoint is not None: result["endpoint"] = endpoint
        if reason is not None: result["reason"] = reason
        if detail: result["detail"] = str(detail)[:600]
        if status == "ready":
            result["message"] = {
                "en": "Wireless access verified and saved: " + endpoint + ". Log collection can reuse it without USB. A headset restart or Wi-Fi change may require USB setup again.",
                "de": "WLAN-Zugriff geprüft und gespeichert: " + endpoint + ". Die Log-Erfassung kann ihn ohne USB nutzen. Nach einem Quest-Neustart oder WLAN-Wechsel kann USB erneut nötig sein."}
        elif status == "not-usb":
            result["message"] = {"en": "The current connection is already wireless; no USB wireless setup was needed.",
                                 "de": "Die aktuelle Verbindung nutzt bereits WLAN; keine WLAN-Einrichtung per USB nötig."}
        else:
            result["message"] = {
                "en": "USB work completed. Wireless access could not be verified; the previous connection settings were retained. Keep the Quest and PC on the same network, then use wireless setup if needed.",
                "de": "USB-Vorgang abgeschlossen. WLAN-Zugriff konnte nicht bestätigt werden; bisherige Verbindungseinstellungen bleiben erhalten. Quest und PC im selben Netzwerk nutzen und bei Bedarf WLAN einrichten."}
        return settings, result

    def run(*args, **options):
        remaining = deadline - time.monotonic()
        if remaining <= 0: raise TimeoutError("Wireless setup's total time budget expired.")
        return adb.run(*args, timeout=min(2, remaining), reconnect=False, **options)

    def identity(address):
        model = run("-s", address, "shell", "getprop", "ro.product.model")
        product = run("-s", address, "shell", "getprop", "ro.product.name")
        if "quest" not in model.lower() and "quest" not in product.lower():
            raise _IdentityChanged("The wireless address is not a recognized Meta Quest.")
        actual = run("-s", address, "shell", "getprop", "ro.serialno")
        if actual != hardware:
            raise _IdentityChanged("The connection belongs to a different headset.")

    def qualify(address, already_connected=False):
        if not already_connected:
            run("connect", address, positive=r"\b(?:already )?connected to\s+" + re.escape(address) + r"(?:\s|$)")
        identity(address)
        settings.update(endpoint=address, wirelessVerifiedUtc=time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()))
        return finished("ready", endpoint=address)

    def normal_failure(error):
        # A cancelled browser action must not be converted to a successful
        # Connect result merely because wireless setup itself is best effort.
        if getattr(error, "code", None) == "cancelled": raise error

    if ":" in serial or serial.startswith("adb-") or ":" in adb.transport(serial):
        return finished("not-usb")
    if config.get("deviceSerial") not in (None, hardware):
        return finished("unavailable", reason="different-headset")
    if not isinstance(hardware, str) or not re.fullmatch(r"[A-Za-z0-9_-]{1,160}", hardware):
        return finished("unavailable", reason="invalid-identity")
    try:
        identity(serial)
        route = run("-s", serial, "shell", "ip", "route")
        ips = {match for match in re.findall(r"\bdev\s+wlan\S*\b[^\n]*\bsrc\s+(\d+\.\d+\.\d+\.\d+)\b", route)
               if _ipv4_endpoint(match + ":5555")}
        if len(ips) != 1:
            return finished("unavailable", reason="wifi-address-unavailable")
        host = ips.pop()
        devices = run("devices", "-l", check_text=False)
        connected = sorted({address for line in devices.splitlines()
                            if len(parts := line.split()) >= 2 and parts[1] == "device"
                            and (address := _ipv4_endpoint(parts[0])) is not None
                            and address.rsplit(":", 1)[0] == host})
        # Do not connect to arbitrary listed peers: only transports at this
        # exact USB Quest's own observed wlan address are candidates.
        if len(connected) > 4:
            return finished("unavailable", reason="ambiguous-listeners")
        for address in connected:
            try: return qualify(address, already_connected=True)
            except _IdentityChanged: raise
            except (RuntimeError, OSError, ValueError, subprocess.TimeoutExpired) as error:
                normal_failure(error)
        if not enable:
            return finished("unavailable", reason="wireless-not-connected")
        port = run("-s", serial, "shell", "getprop", "service.adb.tcp.port").strip()
        listener = int(port) if re.fullmatch(r"\d{1,5}", port) and 1 <= int(port) <= 65535 else None
        address = host + ":" + str(listener or 5555)
        try: return qualify(address)
        except _IdentityChanged: raise
        except (RuntimeError, OSError, ValueError, subprocess.TimeoutExpired) as error:
            normal_failure(error)
            # A configured listener that cannot be reached is a network/ADB
            # authorization issue, not a reason to restart the USB daemon.
            if listener: return finished("unavailable", reason="listener-unreachable", detail=error)
        # Recheck the exact cable identity immediately before changing adbd.
        identity(serial)
        attempted = True
        run("-s", serial, "tcpip", "5555", positive=r"restarting in TCP mode")
        last = None
        for attempt in range(3):
            try: return qualify(address)
            except _IdentityChanged: raise
            except (RuntimeError, OSError, ValueError, subprocess.TimeoutExpired) as error:
                normal_failure(error)
                last = error
                remaining = deadline - time.monotonic()
                if remaining <= 0: break
                if attempt < 2: time.sleep(min(.25 * (attempt + 1), remaining))
        return finished("unavailable", reason="wifi-connect-unavailable", detail=last)
    except (RuntimeError, OSError, ValueError, subprocess.TimeoutExpired) as error:
        normal_failure(error)
        return finished("unavailable", reason="identity-changed" if isinstance(error, _IdentityChanged)
                        else "wireless-check-unavailable", detail=error)
