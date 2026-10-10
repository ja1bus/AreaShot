# Third-party notices / 第三者ソフトウェアについて

The MIT License in [LICENSE](LICENSE) applies only to the source code and files in this repository authored by ja1bus. It does not change the license of the third-party software listed below.

[LICENSE](LICENSE) の MIT License は、このリポジトリに含まれる ja1bus が作成したソースコードとファイルだけに適用されます。以下の第三者のソフトウェアのライセンスは変更しません。

## Runtime / 実行時

AreaShot uses only .NET 8 and its Windows Desktop shared framework (WPF, Windows Forms, System.Text.Json). They are provided by Microsoft under the MIT License and are not included in this repository.

AreaShot が使うのは .NET 8 と Windows Desktop 共有フレームワーク（WPF・Windows Forms・System.Text.Json）だけです。これらは Microsoft が MIT License で提供しているもので、このリポジトリには含まれていません。

- .NET: https://github.com/dotnet/runtime (MIT)
- WPF: https://github.com/dotnet/wpf (MIT)
- Windows Forms: https://github.com/dotnet/winforms (MIT)

Fonts are not bundled. The editor uses fonts installed in Windows.

フォントは同梱していません。エディタは Windows にインストールされているフォントを使います。

## Tests only / テストのみ

The following NuGet packages are referenced by `Tests/AreaShot.Tests.csproj`. They are restored from nuget.org at build time, are not included in this repository, and are not part of the app.

次の NuGet パッケージは `Tests/AreaShot.Tests.csproj` から参照しています。ビルド時に nuget.org から取得するもので、このリポジトリにもアプリにも含まれていません。

| Package | Version | License |
|---|---|---|
| Microsoft.NET.Test.Sdk | 17.11.1 | MIT |
| xunit | 2.9.2 | Apache-2.0 |
| xunit.runner.visualstudio | 2.8.2 | Apache-2.0 |
