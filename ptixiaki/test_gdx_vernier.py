from gdx import gdx
import time

g = gdx.gdx()
g.open(connection='usb')          # αν δεν δει USB, άλλαξε σε 'ble'
g.select_sensors([1])             # GDX-RB: Channel 1 = Force (N)
g.start(50)                       # περίοδος σε ms (~20 Hz)

print("Διαβάζω... (Ctrl+C για διακοπή)")
try:
    while True:
        m = g.read()              # λίστα τιμών από τα ενεργά κανάλια
        if m is None:             # όταν σταματήσει το stream
            break
        print("Force (N):", m[0])
        time.sleep(0.01)
except KeyboardInterrupt:
    pass
finally:
    g.stop(); g.close()
