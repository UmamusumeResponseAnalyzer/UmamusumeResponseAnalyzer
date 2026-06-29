namespace UmamusumeResponseAnalyzer.LiveDisplay
{
    // 把"有新 UI 内容到达"合并成单一可 await 的信号。
    //
    // 渲染循环（UiHost.RunLiveDisplayUntilConsoleInteractionAsync）跑在 Spectre.Live 的
    // 回调里，需要同时响应两类事件源（events channel 与 consoleInteractions channel），
    // 但 SingleReader channel 不能同时被 ReadAsync。于是生产端写完 channel 调 Signal()，
    // 消费端每轮 await WaitForAsync()——它会在"被 Signal 点亮"或"刷新间隔超时"二者其一返回。
    //
    // signalReady 处理"Signal 在 WaitFor 之前到达"的竞态：预点燃后下一次 WaitFor 立即返回。
    internal sealed class UiRefreshSignal
    {
        static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(150);

        readonly object sync = new();
        TaskCompletionSource? pendingSignal;
        bool signalReady;

        // 等待"新内容到达"或"刷新超时"，二者其一即返回。
        public async Task WaitForAsync(CancellationToken cancellationToken)
        {
            TaskCompletionSource signal;
            lock (sync)
            {
                if (signalReady)
                {
                    signalReady = false;
                    return;
                }

                signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                pendingSignal = signal;
            }

            using var registration = cancellationToken.Register(static s => ((TaskCompletionSource)s!).TrySetCanceled(), signal);
            var refresh = Task.Delay(RefreshInterval, cancellationToken);

            var completed = await Task.WhenAny(signal.Task, refresh);
            if (completed == signal.Task)
            {
                await signal.Task;
                return;
            }

            lock (sync)
            {
                if (ReferenceEquals(pendingSignal, signal))
                    pendingSignal = null;
            }
        }

        public void Signal()
        {
            TaskCompletionSource? signal;
            lock (sync)
            {
                signal = pendingSignal;
                if (signal is null)
                {
                    signalReady = true;
                    return;
                }

                pendingSignal = null;
            }

            signal.TrySetResult();
        }
    }
}
