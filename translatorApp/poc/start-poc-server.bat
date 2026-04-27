@echo off
chcp 65001 >nul
echo ========================================
echo  TranslatorApp POC ローカルサーバー
echo ========================================
echo.
echo POCフォルダでHTTPサーバーを起動します
echo ブラウザで http://localhost:5500 にアクセスしてください
echo.
echo Ctrl+C で終了できます
echo.
echo ----------------------------------------

cd /d "%~dp0"
python -m http.server 5500

pause

