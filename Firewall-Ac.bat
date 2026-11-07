@echo off
chcp 65001 > nul
:: Telefonlarin bu kompüterdeki Kvizo-ya qosula bilmesi ucun Windows Firewall-da 5080 portunu acir.
:: Bu fayli SAG KLIK -> "Yonetici olaraq calistir" ile isledin (administrator huququ lazimdir).
:: Hotspot sebekesi Windows-da bezen "Public" sayilir, ona gore butun profillere icaze verilir.
:: Yalniz ev/mekteb sebekesi ucun istesez "profile=any" yerine "profile=private" yazin.

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Bu fayl administrator kimi isledilmelidir.
    pause
    exit /b 1
)

netsh advfirewall firewall delete rule name="Kvizo 5080" >nul 2>&1
netsh advfirewall firewall add rule name="Kvizo 5080" dir=in action=allow protocol=TCP localport=5080 profile=any
echo.
echo Hazirdir. Port 5080 acildi.
echo Silmek ucun: netsh advfirewall firewall delete rule name="Kvizo 5080"
pause
