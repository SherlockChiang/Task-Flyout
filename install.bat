@echo off
chcp 65001 >nul
color 0A
title Task Flyout Installer

echo ========================================================
echo                  Task Flyout Installer
echo ========================================================
echo.
echo 正在请求管理员权限... / Requesting Administrator privileges...

>nul 2>&1 "%SYSTEMROOT%\system32\cacls.exe" "%SYSTEMROOT%\system32\config\system"
if '%errorlevel%' NEQ '0' (
    echo Set UAC = CreateObject^("Shell.Application"^) > "%temp%\getadmin.vbs"
    echo UAC.ShellExecute "%~s0", "", "", "runas", 1 >> "%temp%\getadmin.vbs"
    "%temp%\getadmin.vbs"
    del "%temp%\getadmin.vbs"
    exit /B
)

pushd "%CD%"
CD /D "%~dp0"

echo.
echo [1/2] 正在向系统导入受信任的证书...
echo       Importing trusted certificate to the system...

for %%f in (*.cer) do (
    certutil -addstore TrustedPeople "%%f" >nul 2>&1
    if errorlevel 1 (
        echo.
        echo [错误] 证书导入失败，请确保您以管理员身份运行。
        echo [Error] Certificate import failed. Please run as Administrator.
        pause
        exit /B
    )
)
echo       证书导入成功！ / Certificate imported successfully!

echo.
echo [2/2] 正在安装 Task Flyout 应用程序包...
echo       Installing Task Flyout application package...

for %%f in (*.msixbundle *.msix) do (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-AppxPackage -Path '%%f'"
)

echo.
echo ========================================================
echo     安装完成！您现在可以在“开始”菜单中找到并运行它。
echo     Installation complete! You can find it in the Start menu.
echo ========================================================
echo.
pause