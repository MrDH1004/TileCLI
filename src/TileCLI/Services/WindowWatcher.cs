namespace TileCLI.Services;

/// <summary>
/// 감시 모드: 저주파 폴링(기본 1.2초)으로 <see cref="Changed"/>를 주기 발생시킨다.
/// 실제 "변화 감지"(터미널 집합 비교)와 재배치는 소비자(MainForm)가 판단한다.
///
/// 전역 WinEvent(EVENT_OBJECT_SHOW) 훅은 시스템 전역 오브젝트 이벤트가 UI 스레드로 폭주해
/// 창을 마비시키므로 쓰지 않는다. 폴링은 새 창 감지 지연이 최대 폴 간격이지만 UI를 막지 않는다.
/// System.Windows.Forms.Timer라 UI 스레드에서 틱 → 콜백에서 UI 접근 안전.
/// </summary>
public sealed class WindowWatcher : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer;

    /// <summary>폴 간격마다 발생. 소비자가 억제 구간/집합 변화 여부를 판단해 재배치한다.</summary>
    public event Action? Changed;

    public bool Enabled { get; private set; }

    public WindowWatcher(int intervalMs = 1200)
    {
        _timer = new System.Windows.Forms.Timer { Interval = Math.Max(300, intervalMs) };
        _timer.Tick += (_, _) => { try { Changed?.Invoke(); } catch { /* 소비자 예외 격리 */ } };
    }

    public void SetEnabled(bool on)
    {
        if (on == Enabled) return;
        Enabled = on;
        if (on) _timer.Start(); else _timer.Stop();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
