@echo off
setlocal enabledelayedexpansion

:: ----------------------------------------
:: 固定の基本設定（フォルダの場所）
:: ----------------------------------------
cd /d "%~dp0"

echo ==========================================
echo           Git自動設定 ＆ プッシュ
echo ==========================================

:: 【追加】エラーの原因になるロックファイルを自動削除する
if exist ".git\index.lock" (
    del /f /q ".git\index.lock" >nul 2>&1
    echo [修復] 残っていたGitのロックを解除しました。
)

:: 1. コミットするユーザー名の変更（いじる設定）
set /p change_user="コミットするユーザー名（現在: gaizitti）を変更しますか？ (y/n): "
if /i "%change_user%"=="y" (
    set /p new_name="新しいユーザー名を入力してください: "
    git config --local user.name "!new_name!"
    echo ユーザー名を 「!new_name!」 に変更しました。
    echo.
)

:: 2. ファイルの追加
git add .

:: 3. コミットメッセージの入力
echo.
set /p user_msg="メッセージを入力してください: "

set commit_msg=%user_msg%

:: コミットの実行
git commit -m "%commit_msg%"

:: 4. プッシュの実行
echo.
echo GitHubへ送信中...
git push origin master

echo ==========================================
echo プッシュが完了しました！
echo ==========================================
pause
