@echo off
chcp 65001 >nul
echo ========================================
echo  TranslatorApp POC テストファイルを開く
echo ========================================
echo.

cd /d "%~dp0"

echo POC-1: フットスイッチ検証を開いています...
start chrome "file:///%~dp0poc-footswitch.html"

echo.
echo POC-1をChromeで開きました。
echo.
echo 他のPOCを開く場合:
echo   - POC-2 (Web Audio): poc-web-audio.html
echo   - POC-6 (Entra ID):  poc-entra-auth.html
echo.
echo Python POCを実行する場合:
echo   1. pip install -r requirements.txt
echo   2. 環境変数を設定（README.md参照）
echo   3. python poc_azure_stt.py / poc_deepl.py / poc_azure_openai.py
echo.
pause

