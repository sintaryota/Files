# Folder History

履歴フォルダを自動で表示する、小さな常駐アプリです。Files のサイドバーの「履歴」機能を、単体のアプリとして取り出したものです。

最近使ったフォルダと、よく使うフォルダを、分岐点だけを残したツリーで表示します。**ダブルクリックで、そのフォルダをエクスプローラーで開きます。**

## 使い方

| 操作 | 動作 |
|---|---|
| ダブルクリック / Enter | フォルダをエクスプローラーで開く |
| 右クリック | 「エクスプローラーで開く」「パスをコピー」 |
| マウスを載せる | フルパスと最終使用日時 |
| ツールバー「更新」/ F5 | すぐに取り込み直す |
| ツールバー「常に手前」 | 他のウィンドウの手前に表示する（初期はオン） |
| ツールバー「自動起動」 | Windows のサインイン時に、トレイだけで起動する（初期はオン） |
| ツールバー「診断」 | 履歴に載った理由、載らなかった理由をテキストで表示する |
| ウィンドウの「×」 | トレイに隠す（アプリは動き続ける） |
| トレイのアイコン | 左クリックで表示 / 非表示、右クリックで「終了」 |

アプリが動いている間に、ショートカットなどでもう一度起動すると、2つ目は起動せず、動いているウィンドウが手前に出ます。

## 履歴の集め方

- Windows の「最近使った項目」（`%APPDATA%\Microsoft\Windows\Recent`）から、開いたファイルを取り込みます。アプリが終了している間の分も、次の起動時に取り込みます。
- Word の最近使ったファイルを、1分ごとに確認します。
- エクスプローラーでの移動・コピー・名前変更は、Windows に記録が残らないため取り込めません。
- OneDrive や SharePoint のファイル（URL）は対象外です。

表示するフォルダは、最近使った5件と、使用回数の多いものから、最大15件を選びます。上位のフォルダは、配下のフォルダが選ばれると隠れます。ドライブごとの共通の親フォルダを根にして、その下に、分岐点と、選んだフォルダだけを残します。

## データの場所

`%LOCALAPPDATA%\FolderHistory`（`folder_history.json`、`settings.json`、`report.txt`）。履歴は、この端末の中だけで扱い、外部へは送信しません。

## ビルドと配布

.NET 10 SDK が必要です。

```
dotnet build tools/FolderHistory -c Release
```

配布用の exe は、次のコマンドで作ります。

```
dotnet publish tools/FolderHistory -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

`tools\FolderHistory\bin\Release\net10.0-windows\win-x64\publish\FolderHistory.exe` ができます。**動かす端末には、.NET 10 のデスクトップ ランタイムが必要です。**

```
winget install Microsoft.DotNet.DesktopRuntime.10
```

## ライセンス

MIT License。Copyright (c) koji inoguchi(nagasaki)。リポジトリのルートの `LICENSE-MIT` と `FORK_NOTICE.md` を参照してください。
