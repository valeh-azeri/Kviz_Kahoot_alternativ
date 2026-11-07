@echo off
chcp 65001 > nul
title Kvizo
cd /d "%~dp0"

echo.
echo  ==============================================
echo    Kvizo baslayir. Bu pencereni baglamayin.
echo    Dayandirmaq ucun Ctrl+C basin.
echo  ==============================================
echo.
echo  Ilk acilis 1-2 deqiqe cekir (proqram qurulur, baza yaradilir).
echo  Sayt hazir olan kimi brauzer ozu acilacaq.
echo  Telefonlar ucun sebeke unvani asagida yazilacaq.
echo.

where dotnet >nul 2>&1
if errorlevel 1 (
    echo  XETA: .NET SDK tapilmadi. .NET 10 SDK qurun: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

:: Evvelceden acilmis Kvizo varsa dayandir: o, qurulus fayllarini ve 5080 portunu tutur
:: ve yeni qurulus "faylini basqa proses istifade edir" xetasi ile dayanir.
tasklist /FI "IMAGENAME eq Kviz.Web.exe" 2>nul | find /I "Kviz.Web.exe" >nul
if not errorlevel 1 (
    echo  Evvelki Kvizo hele isleyir. Yeni versiya ucun o dayandirilir...
    echo  ^(Davam eden canli oyun varsa, o legv olunur.^)
    taskkill /F /IM Kviz.Web.exe >nul 2>&1
    timeout /t 3 /nobreak >nul
    echo.
)

:: Proqramin ozu hazir olan kimi brauzeri acir (Program.cs: Kviz:OpenBrowser)
set Kviz__OpenBrowser=true

dotnet run --project Kviz.Web -c Release --no-launch-profile --urls http://0.0.0.0:5080

echo.
echo  Kvizo dayandi. Yuxarida xeta yazilibsa onu oxuyun:
echo    - "Cannot open database" / "network-related": SQL Server islemir ve ya bagllanti setri sehvdir
echo      (Kviz.Web\appsettings.json -> ConnectionStrings:Default)
echo    - "address already in use": 5080 portunu basqa proqram (ve ya ikinci Kvizo) tutub
pause
