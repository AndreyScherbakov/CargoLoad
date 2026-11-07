@echo off
chcp 866 >nul
setlocal

rem ============================================================
rem  Сборка CargoLoader.exe
rem
rem  Использование:
rem    build.bat           - один self-contained .exe (рекомендуется)
rem    build.bat folder    - папка с .exe и библиотеками
rem    build.bat fast      - маленький .exe (нужен .NET 8 Desktop Runtime)
rem ============================================================

set "ROOT=%~dp0"
set "PROJ=%ROOT%CargoLoad\CargoLoad.csproj"
set "EXENAME=CargoLoader"
set "MODE=%~1"
if "%MODE%"=="" set "MODE=onefile"
set "OUT=%ROOT%dist"

echo.
echo === Сборка CargoLoader.exe (режим: %MODE%) ===
echo.

where dotnet >nul 2>nul
if errorlevel 1 goto nodotnet
if not exist "%PROJ%" goto noproj

echo [1/3] Очистка папки dist...
if exist "%OUT%" rd /s /q "%OUT%"
mkdir "%OUT%" 2>nul

echo [2/3] Публикация (Release, win-x64)...
echo.
set "COMMON=-c Release -r win-x64 --nologo -p:AssemblyName=%EXENAME% -p:DebugType=None -p:DebugSymbols=false -o "%OUT%""

if /i "%MODE%"=="folder" goto folder
if /i "%MODE%"=="fast" goto fast
set "FLAGS=--self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true"
goto run

:folder
set "FLAGS=--self-contained true -p:PublishSingleFile=false"
goto run

:fast
set "FLAGS=--self-contained false -p:PublishSingleFile=true"
goto run

:run
dotnet publish "%PROJ%" %FLAGS% %COMMON%
if errorlevel 1 goto failed

echo.
echo [3/3] Проверка результата...
if not exist "%OUT%\%EXENAME%.exe" goto failed
echo.
echo === ГОТОВО ===
echo Файл: %OUT%\%EXENAME%.exe
for %%F in ("%OUT%\%EXENAME%.exe") do echo Размер: %%~zF байт
echo.
if /i "%MODE%"=="fast" echo Внимание: этому варианту нужен установленный .NET 8 Desktop Runtime.
pause
exit /b 0

:nodotnet
echo ОШИБКА: не найден .NET SDK (команда dotnet).
echo Установите .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
pause
exit /b 1

:noproj
echo ОШИБКА: не найден файл проекта: %PROJ%
pause
exit /b 1

:failed
echo.
echo ОШИБКА СБОРКИ. Смотрите сообщения выше.
pause
exit /b 1