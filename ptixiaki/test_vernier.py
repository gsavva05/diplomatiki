from godirect import GoDirect
import time

gd = GoDirect(use_usb=True, use_ble=False)  # πρώτα USB
devices = gd.list_devices()
if not devices:
    print("Δεν βρέθηκε συσκευή! Βεβαιώσου για USB/καλώδιο ή δοκίμασε BLE.")
    gd.quit()
    raise SystemExit

dev = devices[0]
dev.open(auto_start=True)
print("Βρήκα τη ζώνη:", dev)

# --- sensors ---
print("Διαθέσιμοι αισθητήρες:")
for k, s in dev.sensors.items():
    print(f" - key={k}, sensor={s}")

sensor = next(iter(dev.sensors.values()))
print("Χρησιμοποιώ αισθητήρα:", sensor)

print("Διαβάζω δεδομένα... (Ctrl+C για διακοπή)")
try:
    while True:
        val = sensor.read()
        if val is not None:
            print("Τιμή ζώνης:", val)
        time.sleep(0.05)
except KeyboardInterrupt:
    pass
finally:
    dev.close()
    gd.quit()
    print("Τέλος")
