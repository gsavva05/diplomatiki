@echo off
cd /d "%USERPROFILE%\Desktop\ptixiaki"
py -3.13 stream_to_udp_gdx.py --conn ble --name "GDX-RB" --period_ms 50
pause
