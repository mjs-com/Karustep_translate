@echo off
chcp 65001 >nul
echo ========================================
echo  TranslatorApp Backend Server
echo ========================================
echo.

cd /d "%~dp0backend"

REM 仮想環境の確認
if exist "venv\Scripts\activate.bat" (
    echo 仮想環境を使用します...
    call venv\Scripts\activate.bat
) else (
    echo 仮想環境が見つかりません。グローバルPythonを使用します。
)

REM 依存パッケージの確認
echo 依存パッケージをインストール中...
pip install -r requirements.txt -q

REM .envファイルの確認
if not exist ".env" (
    echo.
    echo ⚠️ .env ファイルが見つかりません
    echo .env.example をコピーして .env を作成し、APIキーを設定してください
    echo.
    pause
    exit /b 1
)

echo.
echo サーバーを起動中... (http://localhost:8000)
echo Ctrl+C で終了
echo.

python -m uvicorn main:app --host 0.0.0.0 --port 8000 --reload

pause

