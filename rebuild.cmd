@echo off
REM ============================================================
REM  CMTS 清理重建脚本
REM
REM  用途：
REM    1. 清掉 bin / obj 中间产物（Visual Studio 的 XAML 智能
REM       感知缓存过期时，会报一堆 CS0103"不存在名称 XXX"的
REM       假错误，重建即可恢复）
REM    2. 关掉正在运行的 CMTS（否则 exe 被占用，编译会报
REM       MSB3021 / MSB3027）
REM
REM  用法：双击本文件，或在 CMD 里执行 rebuild.cmd
REM
REM  注意：bin 和 obj 都是可再生的中间产物，删掉不会丢源码。
REM        它们也已经在 .gitignore 里，不会影响 Git。
REM ============================================================

setlocal
cd /d "%~dp0"

echo.
echo ============================================================
echo  CMTS 清理重建
echo  目录: %CD%
echo ============================================================
echo.

REM ---------- 1. 关掉正在运行的 CMTS ----------
echo [1/3] 检查是否有 CMTS 正在运行...

tasklist /FI "IMAGENAME eq Chassis Master Test Suite.exe" 2>nul | find /I "Chassis Master Test Suite.exe" >nul

if %ERRORLEVEL% EQU 0 (
    echo       发现正在运行，正在关闭...
    taskkill /IM "Chassis Master Test Suite.exe" /F >nul 2>&1
    timeout /t 2 /nobreak >nul
    echo       已关闭。
) else (
    echo       没有正在运行的实例。
)

REM ---------- 2. 清理 bin / obj ----------
echo.
echo [2/3] 清理 bin 和 obj...

if exist "bin" (
    rmdir /s /q "bin"
    if exist "bin" (
        echo       [警告] bin 删除失败，可能仍被占用。
        echo              请手动关闭 Visual Studio 后重试。
    ) else (
        echo       bin 已删除。
    )
) else (
    echo       bin 不存在，跳过。
)

if exist "obj" (
    rmdir /s /q "obj"
    if exist "obj" (
        echo       [警告] obj 删除失败，可能仍被占用。
        echo              请手动关闭 Visual Studio 后重试。
    ) else (
        echo       obj 已删除。
    )
) else (
    echo       obj 不存在，跳过。
)

REM ---------- 3. 重新编译 ----------
echo.
echo [3/3] 重新编译...
echo.

dotnet build "Chassis Master Test Suite.csproj" --nologo

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo ============================================================
    echo  编译失败，请看上面的错误信息。
    echo ============================================================
    echo.
    pause
    exit /b 1
)

echo.
echo ============================================================
echo  完成：编译成功，0 错误。
echo.
echo  如果 Visual Studio 里还显示旧的错误列表，
echo  请在 VS 中执行：生成 -^> 重新生成解决方案
echo  ^(或关闭再重新打开解决方案^)
echo ============================================================
echo.

pause
endlocal
