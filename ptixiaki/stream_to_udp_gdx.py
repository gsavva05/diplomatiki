#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import argparse, asyncio, sys, time, json, socket
from contextlib import suppress

# pip install vernier-gdx bleak
from gdx import gdx
try:
    from bleak import BleakScanner
    _BLEAK_OK = True
except Exception:
    _BLEAK_OK = False

def now_iso():
    return time.strftime("%Y-%m-%dT%H:%M:%S", time.gmtime())

async def scan_ble_names(prefixes, timeout=5.0):
    if not _BLEAK_OK:
        print("[BLE] Το 'bleak' δεν είναι εγκατεστημένο.")
        return []
    devs = await BleakScanner.discover(timeout=timeout)
    names = []
    for d in devs:
        n = (d.name or "").strip()
        if not n: continue
        if not prefixes or any(n.startswith(p) for p in prefixes):
            if n not in names: names.append(n)
    return names

class GDXDual:
    def __init__(self, transport, prefixes, period_ms, udp_host=None, udp_port=None):
        self.transport = transport.lower()   # auto|usb|ble
        self.prefixes = prefixes
        self.period_ms = period_ms
        self.udp_host = udp_host
        self.udp_port = udp_port
        self.g = gdx.gdx()
        self.sock = None
        if udp_host and udp_port:
            self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    # ---------- OPEN ----------
    def open_usb(self):
        print("[GDX] Άνοιγμα μέσω USB…")
        self.g.open_usb()
        return "USB"

    def open_ble(self, device_name):
        print(f"[GDX] Άνοιγμα μέσω BLE: {device_name}")
        self.g.open_ble(device_to_open=device_name)
        return "BLE"

    def auto_open(self):
        with suppress(Exception):
            t = self.open_usb()
            return t, None
        if not _BLEAK_OK:
            raise RuntimeError("Αποτυχία USB και δεν υπάρχει 'bleak' για BLE.")
        names = asyncio.get_event_loop().run_until_complete(
            scan_ble_names(self.prefixes, timeout=5.0)
        )
        print(f"[BLE] Βρέθηκαν: {names or '[]'}")
        if not names:
            raise RuntimeError("Δεν βρέθηκε BLE συσκευή που να ταιριάζει στα prefixes.")
        device = next((n for n in names if n.startswith("GDX-RB")), names[0])
        t = self.open_ble(device)
        return t, device

    def connect(self):
        if self.transport == "usb":
            return self.open_usb(), None
        if self.transport == "ble":
            if not _BLEAK_OK:
                raise RuntimeError("Ζητήθηκε BLE αλλά δεν υπάρχει 'bleak'.")
            names = asyncio.get_event_loop().run_until_complete(
                scan_ble_names(self.prefixes, timeout=5.0)
            )
            print(f"[BLE] Βρέθηκαν: {names or '[]'}")
            if not names:
                raise RuntimeError("Δεν βρέθηκε BLE συσκευή που να ταιριάζει στα prefixes.")
            device = next((n for n in names if n.startswith("GDX-RB")), names[0])
            return self.open_ble(device), device
        return self.auto_open()

    # ---------- SENSORS ----------
    def select_sensors(self, device_name):
        """
        GDX-RB: επιλέγουμε αυτόματα **Force (κανάλι 1)** για συνεχές σήμα.
        Άλλες Go Direct: auto (αν δεν ξέρουμε τα κανάλια).
        """
        if device_name is None:
            # USB: αν είναι RB θα το καταλάβουμε στην πρώτη ανάγνωση – αλλά καλύτερα δοκίμασε [1]
            try:
                self.g.select_sensors([1])
                print("[GDX] Επιλογή αισθητήρων: [1] (Force, continuous)")
                return
            except Exception:
                pass

        if device_name and str(device_name).startswith("GDX-RB"):
            self.g.select_sensors([1])  # Force
            print("[GDX] Επιλογή αισθητήρων: [1] (Force, continuous)")
        else:
            self.g.select_sensors()
            print("[GDX] Επιλογή αισθητήρων: auto")

    # ---------- STREAM ----------
    def start(self):
        self.g.start(period=self.period_ms)
        if self.sock:
            print(f"[GDX] Streaming UDP σε {self.udp_host}:{self.udp_port} κάθε {self.period_ms} ms")
        else:
            print(f"[GDX] Έναρξη δειγματοληψίας κάθε {self.period_ms} ms")

    def emit(self, payload: dict):
        if self.sock:
            self.sock.sendto(json.dumps(payload).encode("utf-8"), (self.udp_host, self.udp_port))
        else:
            val = payload.get("metrics", {}).get("value")
            if val is not None:
                print(f"[{payload['ts_iso']}] value={val:.3f}")

    def loop(self, transport_used, device_name):
        per_s = self.period_ms / 1000.0
        model = "GDX-RB" if (device_name or "").startswith("GDX-RB") else "GoDirect"
        try:
            while True:
                row = self.g.read()
                if row is None:
                    time.sleep(per_s); continue
                # Force (κανάλι 1) – συνεχές σήμα
                val = None
                try:
                    if isinstance(row, (list, tuple)) and len(row):
                        val = float(row[0])
                except:
                    val = None

                payload = {
                    "schema": "ptix.godirect.v1",
                    "ts_unix": round(time.time(), 3),
                    "ts_iso": now_iso(),
                    "device": {"name": device_name or "USB", "transport": transport_used, "model": model},
                    "sampling_ms": self.period_ms,
                    "metrics": {"value": None if val is None else round(val, 3)}  # continuous value
                }
                self.emit(payload)
                time.sleep(per_s)
        except KeyboardInterrupt:
            print("\n[SYS] Διακοπή.")
        finally:
            with suppress(Exception): self.g.stop()
            with suppress(Exception): self.g.close()
            print("[GDX] Σταμάτησε.")

def main():
    ap = argparse.ArgumentParser("GDX Dual (USB/BLE) streamer (Force ch1 auto για RB)")
    ap.add_argument("--transport", choices=["auto","usb","ble"], default="auto")
    ap.add_argument("--prefix", action="append", default=["GDX-RB"], help="BLE name prefixes (repeatable). --prefix \"\" για όλα.")
    ap.add_argument("--period-ms", type=int, default=50)
    ap.add_argument("--udp-host", default="127.0.0.1")
    ap.add_argument("--udp-port", type=int, default=5005)
    args = ap.parse_args()

    prefixes = args.prefix
    if len(prefixes) == 1 and prefixes[0] == "": prefixes = []

    g = GDXDual(args.transport, prefixes, args.period_ms, args.udp_host, args.udp_port)
    try:
        transport_used, device_name = g.connect()
    except Exception as e:
        print(f"[ERR] Αποτυχία σύνδεσης: {e}"); sys.exit(2)

    with suppress(Exception): g.select_sensors(device_name)
    g.start()
    g.loop(transport_used, device_name)

if __name__ == "__main__":
    main()
