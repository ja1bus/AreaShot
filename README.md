# AreaShot

[English](README.en.md) | 日本語

AreaShot は、Windows 11 向けの軽量なスクリーンショットツールです。タスクトレイに常駐し、ショートカット（既定 `Ctrl+Alt+S`）で画面の範囲を指定して撮影し、クリップボードへのコピー・ファイルへの保存・簡易エディタでの書き込みを素早く行えます。

このリポジトリは、一般公開している AreaShot のソースコードです。

## 主な機能

- ショートカットまたはタスクトレイのメニューから範囲指定キャプチャー（マルチモニター・拡大率の異なるモニターに対応）
- 撮影後のダイアログから、クリップボードにコピー / 保存先に保存 / 名前を付けて保存 / イメージエディタで開く / もう一度撮影
- 修飾キーを押しながら範囲選択を終えると、ダイアログを出さずにコピー・保存・エディタを実行（キーの割り当ては設定で変更可）
- 簡易エディタ: 直線・四角形・矢印・文字・矩形モザイク、元に戻す / やり直し、非破壊編集（元画像と書き込みを分けて保持し、出力時に合成）
- 保存形式は PNG と JPEG（品質指定可）。ファイル名は日時のテンプレート（例 `{yy}{MM}{dd}{HH}{mm}{ss}_capture`）・連番（`001.png`）・連番＋説明（`001_設定を開く.png`。保存時に説明を入力）から選択。連番は手順書・説明書の画像を撮った順に残すためのもの
- Windows へのサインイン時の自動起動（任意）
- 表示言語は英語と日本語（Windows の表示言語に合わせるか、設定で固定）

対象外の機能（全画面・ウィンドウ単位・スクロールキャプチャー、OCR、アップロードなど）はありません。画像や設定はこの PC の中だけで扱い、AreaShot がインターネットへ送信することはありません（詳しくは下記のプライバシーポリシー）。

## 対応 OS と実行環境

- Windows 11（64 ビット、x64）
- Microsoft Store 版は .NET を内包しているため、追加のインストールは不要です。
- このリポジトリのソースからビルドして実行する場合は、[.NET 8 Desktop Runtime（x64）](https://dotnet.microsoft.com/download/dotnet/8.0) が必要です。

## インストール

正式な配布先は Microsoft Store です。通常はこちらから入手してください（無料）。

- Microsoft Store: https://apps.microsoft.com/detail/9PBS7QBWJDD2

この GitHub リポジトリでは実行ファイル（バイナリ）を配布していません。

## 基本的な使い方

1. 起動するとタスクトレイに常駐します（ウィンドウは表示されません）。
2. `Ctrl+Alt+S`（またはトレイアイコンの右クリック →「キャプチャー開始」）で範囲選択を始め、ドラッグで範囲を指定します。`Esc` で取り消します。
3. 表示されたダイアログから、コピー・保存・エディタなどを選びます。
4. ショートカット・保存先・ファイル名・画像形式・表示言語などは、トレイのメニューの「設定...」で変更できます。
5. 終了はトレイのメニューの「終了」です。

詳しい使い方は取扱説明書をご覧ください。

- 取扱説明書: https://www.iwanaga.net/areashot/manual/

## ビルド方法

### 必要な環境

- Windows 11 x64
- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0)（確認済み: 8.0.425）
- `dotnet` CLI だけでビルドできます（Visual Studio は不要）。
- 本体は NuGet パッケージに依存しません。テストだけが NuGet の `Microsoft.NET.Test.Sdk`・`xunit`・`xunit.runner.visualstudio` を使います（初回のビルド時に nuget.org から取得します）。

### コマンド

リポジトリのルートで実行します。

```powershell
dotnet build AreaShot.csproj
dotnet test Tests/AreaShot.Tests.csproj
dotnet run --project AreaShot.csproj
```

`dotnet run` で起動するとタスクトレイに常駐します。終了はトレイのメニューの「終了」です。

配布用のフォルダ一式（Framework-dependent、win-x64）を作るには次を実行します。出力先は `publish\FrameworkDependent\` で、実行には .NET 8 Desktop Runtime（x64）が必要です。

```powershell
dotnet publish AreaShot.csproj -c Release -p:PublishProfile=FrameworkDependent
```

出力には `LICENSE` と `THIRD-PARTY-NOTICES.md` も入ります（v1.2.0〜。アプリの設定画面の「MIT License」「第三者のソフトウェア」から表示します）。

### 構成

| パス | 内容 |
|---|---|
| `AreaShot.csproj` | 本体（WPF、`net8.0-windows`）。タスクトレイだけ WinForms の `NotifyIcon` を使用 |
| `Core/` | 画面・Win32 に依存しないロジックと、画面の文字列（英語 `Strings.resx`・日本語 `Strings.ja.resx`） |
| `Imaging/` | 書き込みの描画・画像の合成・エンコード |
| `Tests/` | `Core` と `Imaging` の単体テスト（xUnit） |
| `Capture/`・`Editor/`・`Services/`・`Views/`・`Interop/` | 範囲選択・エディタ・トレイやショートカットなどの処理・画面・Win32 API の宣言 |
| `icon.png`・`Resources/icon.ico` | アプリアイコン（`pwsh -File tools/make-icon.ps1` で `icon.png` から `icon.ico` を作成） |

Microsoft Store 版のパッケージ（MSIX）を作る仕組みは、このリポジトリに含めていません。`AreaShot.csproj` にある `AreaShotMsix` の条件と `Services/AutoStartService.Packaged.cs` は Store 版のためのものです。

ソースコード中のコメントは主に日本語です。コメント中の「仕様書 §N」は、このリポジトリには含めていない開発用の仕様書の節番号です。

## ライセンス

[MIT License](LICENSE) で公開しています。

Copyright (c) 2026 ja1bus（© ja1bus）

第三者のソフトウェアについては [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) を参照してください。

## 無保証

本ソフトウェアは「現状のまま」提供され、明示・黙示を問わず、いかなる保証もありません。本ソフトウェアの使用によって生じたいかなる損害についても、作者は責任を負いません（詳細は [LICENSE](LICENSE)）。

## 関連リンク

- Web ページ: https://www.iwanaga.net/areashot/
- 取扱説明書: https://www.iwanaga.net/areashot/manual/
- 更新履歴: https://www.iwanaga.net/areashot/changelog/
- プライバシーポリシー: https://www.iwanaga.net/areashot/privacy/
- Microsoft Store: https://apps.microsoft.com/detail/9PBS7QBWJDD2
- お問い合わせ: support@iwanaga.net
