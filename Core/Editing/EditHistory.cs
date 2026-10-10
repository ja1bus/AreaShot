using System.Collections.Immutable;
using AreaShot.Core.Annotations;

namespace AreaShot.Core.Editing;

/// <summary>
/// 編集オブジェクト一覧の Undo / Redo（仕様書 §11）。
///
/// オブジェクトが不変なので、履歴は「一覧のスナップショット」を積むだけでよい（変更されていない要素は共有される）。
/// ドラッグ中の途中経過は <see cref="BeginPreview"/> → <see cref="Preview"/>（何度でも）→ <see cref="CommitPreview"/> で
/// 履歴 1 件にまとめる。
/// </summary>
public sealed class EditHistory
{
    public const int DefaultLimit = 100;

    private readonly List<ImmutableList<Annotation>> _undo = [];
    private readonly List<ImmutableList<Annotation>> _redo = [];
    private ImmutableList<Annotation>? _previewBase;

    public EditHistory(int limit = DefaultLimit)
    {
        Limit = Math.Max(1, limit);
    }

    public int Limit { get; }

    /// <summary>現在の編集オブジェクト一覧（プレビュー中はプレビューの内容）。</summary>
    public ImmutableList<Annotation> Current { get; private set; } = [];

    public bool CanUndo => _previewBase is null && _undo.Count > 0;
    public bool CanRedo => _previewBase is null && _redo.Count > 0;
    public bool IsPreviewing => _previewBase is not null;
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    /// <summary>一覧または Undo / Redo の可否が変わった。</summary>
    public event EventHandler? Changed;

    /// <summary>1 操作として確定する。内容が変わらない場合は履歴に積まない。</summary>
    public void Execute(ImmutableList<Annotation> next)
    {
        if (_previewBase is not null) CommitPreview();
        if (SameContent(Current, next)) return;
        Push(Current);
        Current = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>ドラッグ等の途中経過を始める。</summary>
    public void BeginPreview()
    {
        if (_previewBase is not null) CommitPreview();
        _previewBase = Current;
    }

    /// <summary>途中経過を表示用に反映する（履歴には積まない）。</summary>
    public void Preview(ImmutableList<Annotation> next)
    {
        if (_previewBase is null) BeginPreview();
        Current = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>途中経過を 1 件の履歴として確定する。開始時から変わっていなければ積まない。</summary>
    public void CommitPreview()
    {
        if (_previewBase is null) return;
        var baseline = _previewBase;
        _previewBase = null;
        if (!SameContent(baseline, Current)) Push(baseline);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>途中経過を破棄して開始時の状態へ戻す。</summary>
    public void CancelPreview()
    {
        if (_previewBase is null) return;
        Current = _previewBase;
        _previewBase = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Undo()
    {
        if (!CanUndo) return false;
        _redo.Add(Current);
        Current = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo) return false;
        _undo.Add(Current);
        Current = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void Push(ImmutableList<Annotation> previous)
    {
        _undo.Add(previous);
        if (_undo.Count > Limit) _undo.RemoveAt(0); // 古いものから捨てる
        _redo.Clear();
    }

    private static bool SameContent(ImmutableList<Annotation> a, ImmutableList<Annotation> b) =>
        ReferenceEquals(a, b) || a.SequenceEqual(b);
}
