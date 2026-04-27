@echo off
chcp 65001 >nul
echo ========================================
echo  TranslatorApp Frontend Server
echo ========================================
echo.

cd /d "%~dp0frontend"

REM node_modulesの確認
if not exist "node_modules" (
    echo 依存パッケージをインストール中...
    call npm install
)

echo.
echo 開発サーバーを起動中... (http://localhost:3000)
echo Ctrl+C で終了
echo.

call npm run dev

pause

