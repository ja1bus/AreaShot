namespace AreaShot.Core.Output;

/// <summary>ログファイルの大きさの上限（V1.4.0。仕様書 §17）。</summary>
public static class LogRotation
{
    /// <summary>error.log の上限（1 MB）。</summary>
    public const long DefaultLimitBytes = 1024 * 1024;

    /// <summary>
    /// <paramref name="path"/> が <paramref name="limitBytes"/> を超えていれば <c>path + ".1"</c> へ移す（既存は上書き）。
    /// 移した場合 true。失敗は例外のまま返す（呼び出し側は握ってから、そのまま追記を続ける）。
    /// </summary>
    public static bool RotateIfLarger(string path, long limitBytes = DefaultLimitBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= limitBytes) return false;
        File.Move(path, path + ".1", overwrite: true);
        return true;
    }
}
