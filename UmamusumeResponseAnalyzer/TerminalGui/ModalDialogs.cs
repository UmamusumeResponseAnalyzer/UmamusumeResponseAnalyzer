using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace UmamusumeResponseAnalyzer.TerminalGui;

static class ModalDialogs
{
    const int PromptHeight = 3;

    internal static string PickExecutable(string title, CancellationToken cancellationToken = default)
    {
        var host = TerminalUi.RequireHost();
        if (Environment.CurrentManagedThreadId != host.Application.MainThreadId)
            return InvokeOnOwner(host.OwnerContext, () => PickExecutable(title, cancellationToken));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(host.LifetimeToken, cancellationToken);
        using var picker = new OpenDialog
        {
            Title = title, OpenMode = OpenMode.File, MustExist = true, AllowsMultipleSelection = false,
            AllowedTypes = [new AllowedType("Game executable", [".exe"])]
        };
        Run(host.Application, picker, linked.Token);
        return picker.Canceled ? throw new OperationCanceledException("File selection cancelled.") : picker.Path;
    }

    internal static async Task RunProgressAsync(
        Func<IProgress<DownloadProgress>, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        var host = TerminalUi.RequireHost();
        var app = host.Application;
        var context = host.OwnerContext;
        var lifetimeToken = host.LifetimeToken;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            lifetimeToken,
            cancellationToken);
        await RunProgressAsync(app, context, action, linkedCts.Token);
    }

    static async Task RunProgressAsync(
        IApplication app,
        SynchronizationContext context,
        Func<IProgress<DownloadProgress>, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        if (Environment.CurrentManagedThreadId != app.MainThreadId)
        {
            await InvokeOnOwnerAsync(
                context,
                () => RunProgressAsync(app, context, action, cancellationToken));
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var dialog = CreateDialog("正在处理");
        var body = new View
        {
            X = 1,
            Y = PromptHeight,
            Width = Dim.Fill(1),
            Height = Dim.Fill(2)
        };
        var rows = new Dictionary<string, (Label Label, ProgressBar Bar)>(StringComparer.Ordinal);
        var running = 1;
        var progress = new DialogProgress(
            app,
            () => Volatile.Read(ref running) != 0,
            value =>
        {
            if (!rows.TryGetValue(value.Id, out var row))
            {
                var y = rows.Count;
                row = (
                    new Label
                    {
                        X = 0,
                        Y = y,
                        Width = Dim.Percent(60),
                        Text = value.Description
                    },
                    new ProgressBar
                    {
                        X = Pos.Percent(60),
                        Y = y,
                        Width = Dim.Fill()
                    });
                rows.Add(value.Id, row);
                body.Add(row.Label, row.Bar);
            }

            row.Label.Text = value.Description;
            row.Bar.Fraction = value.Total <= 0
                ? 0
                : Math.Clamp((float)value.Completed / value.Total, 0, 1);
        });

        var userCancelled = 0;
        var cancel = CreateButton("取消", false, () =>
        {
            Interlocked.Exchange(ref userCancelled, 1);
            linkedCts.Cancel();
            app.RequestStop(dialog);
        });
        cancel.X = Pos.Center();
        cancel.Y = Pos.Bottom(body);
        dialog.Add(body, cancel);

        Task? task = null;
        var actionStarted = 0;
        void StartAction(object? sender, Terminal.Gui.App.EventArgs<bool> args)
        {
            if (!args.Value || Interlocked.Exchange(ref actionStarted, 1) != 0)
                return;

            task = Task.Run(
                () => action(progress, linkedCts.Token),
                CancellationToken.None);
            _ = task.ContinueWith(
                _ =>
                {
                    if (Volatile.Read(ref running) != 0)
                        app.Invoke(() =>
                        {
                            if (Volatile.Read(ref running) != 0)
                                app.RequestStop(dialog);
                        });
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        dialog.IsRunningChanged += StartAction;

        Exception? runFailure = null;
        Exception? actionFailure = null;
        try
        {
            Run(app, dialog, linkedCts.Token);
        }
        catch (Exception ex)
        {
            runFailure = ex;
        }
        finally
        {
            dialog.IsRunningChanged -= StartAction;
            Volatile.Write(ref running, 0);
            if (task is not { IsCompleted: true })
            {
                Interlocked.Exchange(ref userCancelled, 1);
                await linkedCts.CancelAsync();
            }
            if (task is not null)
            {
                try
                {
                    await task;
                }
                catch (Exception ex)
                {
                    actionFailure = ex;
                }
            }
        }

        if (task is null)
        {
            if (runFailure is not null)
                ExceptionDispatchInfo.Capture(runFailure).Throw();
            cancellationToken.ThrowIfCancellationRequested();
            throw new OperationCanceledException(linkedCts.Token);
        }

        if (actionFailure is not null &&
            actionFailure is not OperationCanceledException)
        {
            ExceptionDispatchInfo.Capture(actionFailure).Throw();
        }
        if (runFailure is not null &&
            runFailure is not OperationCanceledException)
        {
            ExceptionDispatchInfo.Capture(runFailure).Throw();
        }

        if (Volatile.Read(ref userCancelled) != 0 ||
            cancellationToken.IsCancellationRequested ||
            runFailure is OperationCanceledException)
        {
            throw new OperationCanceledException(
                "操作已取消。",
                actionFailure as OperationCanceledException ?? runFailure,
                cancellationToken.IsCancellationRequested ? cancellationToken : linkedCts.Token);
        }

        if (actionFailure is not null)
            ExceptionDispatchInfo.Capture(actionFailure).Throw();
        if (runFailure is not null)
            ExceptionDispatchInfo.Capture(runFailure).Throw();
        cancellationToken.ThrowIfCancellationRequested();
    }

    internal static T Select<T>(
        string title,
        IEnumerable<T> choices,
        Func<T, string>? converter = null,
        CancellationToken cancellationToken = default)
        => RunOnOwner((app, token) =>
        {
            var values = choices.ToArray();
            if (values.Length == 0)
                throw new ArgumentException("选择列表不能为空。", nameof(choices));

            var index = RunList(
                app,
                title,
                values.Select(x => converter?.Invoke(x) ?? x?.ToString() ?? string.Empty).ToArray(),
                token);
            return values[index];
        }, cancellationToken);

    internal static T Menu<T>(
        string title,
        IEnumerable<T> choices,
        Func<T, string>? converter = null,
        CancellationToken cancellationToken = default)
        => RunOnOwner((app, token) =>
        {
            var values = choices.ToArray();
            if (values.Length == 0)
                throw new ArgumentException("菜单不能为空。", nameof(choices));

            using var window = new Window
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                BorderStyle = null,
                ShadowStyle = ShadowStyles.None
            };
            window.Margin.Thickness = Thickness.Empty;
            window.Add(new Label
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = PromptHeight,
                Text = title
            });
            var selectedIndex = -1;
            var items = values
                .Select((value, index) => new MenuItem
                {
                    Title = converter?.Invoke(value) ?? value?.ToString() ?? string.Empty,
                    Action = () =>
                    {
                        selectedIndex = index;
                        app.RequestStop(window);
                    }
                })
                .ToArray();
            var menu = new Terminal.Gui.Views.Menu(items)
            {
                X = 0,
                Y = PromptHeight,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };
            window.Add(menu);
            items[0].SetFocus();
            Run(app, window, token);
            return selectedIndex >= 0
                ? values[selectedIndex]
                : throw new OperationCanceledException("菜单已取消。");
        }, cancellationToken);

    internal static IReadOnlyList<T> MultiSelect<T>(
        string title,
        IEnumerable<T> choices,
        IEnumerable<T>? selected = null,
        Func<T, string>? converter = null,
        CancellationToken cancellationToken = default)
        => RunOnOwner((app, token) =>
        {
            var values = choices.ToArray();
            if (values.Length == 0)
                throw new ArgumentException("多选列表不能为空。", nameof(choices));

            using var dialog = CreateDialog(title);
            var list = CreateList(values.Select(x => converter?.Invoke(x) ?? x?.ToString() ?? string.Empty));
            list.MarkMultiple = true;
            list.ShowMarks = true;
            var selectedValues = selected?.ToHashSet() ?? [];
            for (var i = 0; i < values.Length; i++)
            {
                if (selectedValues.Contains(values[i]))
                    list.Source?.SetMark(i, true);
            }

            var accepted = false;
            var ok = CreateButton("确定", isDefault: true, () =>
            {
                accepted = true;
                app.RequestStop(dialog);
            });
            var cancel = CreateButton("取消", isDefault: false, () => app.RequestStop(dialog));
            Layout(dialog, list, ok, cancel);
            list.SetFocus();
            Run(app, dialog, token);
            if (!accepted)
                throw new OperationCanceledException("多选已取消。");
            return list.GetAllMarkedItems().Select(x => values[x]).ToArray();
        }, cancellationToken);

    internal static string Ask(
        string title,
        string? value = null,
        bool allowEmpty = false,
        CancellationToken cancellationToken = default)
        => RunOnOwner((app, token) =>
        {
            using var dialog = CreateDialog(title, height: 10);
            var input = new TextField
            {
                Text = value ?? string.Empty,
                X = 1,
                Y = PromptHeight,
                Width = Dim.Fill(1)
            };
            input.MouseHighlightStates |= MouseState.In;
            var accepted = false;
            var ok = CreateButton("确定", true, () =>
            {
                if (allowEmpty || !string.IsNullOrWhiteSpace(input.Text))
                {
                    accepted = true;
                    app.RequestStop(dialog);
                }
            });
            var cancel = CreateButton("取消", false, () => app.RequestStop(dialog));
            ok.X = Pos.Center() - 10;
            ok.Y = Pos.Bottom(input) + 1;
            cancel.X = Pos.Right(ok) + 2;
            cancel.Y = ok.Y;
            dialog.Add(input, ok, cancel);
            input.SetFocus();
            Run(app, dialog, token);

            if (!accepted)
                throw new OperationCanceledException("输入已取消。");
            return input.Text;
        }, cancellationToken);

    internal static bool Confirm(
        string title,
        bool defaultValue = false,
        CancellationToken cancellationToken = default)
        => RunOnOwner((app, token) =>
        {
            using var dialog = CreateDialog(title, height: 9);
            var result = false;
            var yes = CreateButton("是", defaultValue, () =>
            {
                result = true;
                app.RequestStop(dialog);
            });
            var no = CreateButton("否", !defaultValue, () =>
            {
                result = false;
                app.RequestStop(dialog);
            });
            yes.X = Pos.Center() - 8;
            yes.Y = PromptHeight + 1;
            no.X = Pos.Right(yes) + 2;
            no.Y = yes.Y;
            dialog.Add(yes, no);
            (defaultValue ? yes : no).SetFocus();
            Run(app, dialog, token);
            return result;
        }, cancellationToken);

    internal static bool Acknowledge(
        string title = "按 Enter 返回",
        CancellationToken cancellationToken = default)
        => RunOnOwner((app, token) =>
        {
            using var dialog = CreateDialog(title, height: 9);
            var accepted = false;
            var ok = CreateButton("确定", true, () =>
            {
                accepted = true;
                app.RequestStop(dialog);
            });
            ok.X = Pos.Center();
            ok.Y = PromptHeight + 1;
            dialog.Add(ok);
            ok.SetFocus();
            Run(app, dialog, token);
            return accepted;
        }, cancellationToken);

    static int RunList(
        IApplication app,
        string title,
        IReadOnlyList<string> choices,
        CancellationToken cancellationToken)
    {
        using var dialog = CreateDialog(title);
        var list = CreateList(choices);
        var accepted = false;
        var ok = CreateButton("确定", true, () =>
        {
            accepted = true;
            app.RequestStop(dialog);
        });
        var cancel = CreateButton("取消", false, () => app.RequestStop(dialog));
        list.Accepted += (_, _) =>
        {
            accepted = true;
            app.RequestStop(dialog);
        };
        Layout(dialog, list, ok, cancel);
        list.SetFocus();
        Run(app, dialog, cancellationToken);
        if (!accepted)
            throw new OperationCanceledException("选择已取消。");
        return list.SelectedItem ?? 0;
    }

    static void Run(
        IApplication app,
        IRunnable runnable,
        CancellationToken cancellationToken)
    {
        if (Environment.CurrentManagedThreadId != app.MainThreadId)
            throw new InvalidOperationException("Terminal.Gui dialog 必须在 UI owner thread 运行。");

        cancellationToken.ThrowIfCancellationRequested();
        using var cancellationRegistration = cancellationToken.Register(
            () => app.Invoke(() => app.RequestStop(runnable)));
        app.Run(runnable);
        cancellationToken.ThrowIfCancellationRequested();
    }

    static Dialog CreateDialog(string title, int? height = null)
    {
        var dialog = new Dialog
        {
            Title = "URA",
            Width = Dim.Percent(80),
            Height = height is null ? Dim.Percent(80) : height.Value
        };
        dialog.Add(new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = PromptHeight,
            Text = title
        });
        return dialog;
    }

    static ListView CreateList(IEnumerable<string> choices)
    {
        var list = new ListView
        {
            X = 0,
            Y = PromptHeight,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
            CanFocus = true
        };
        list.SetSource(new ObservableCollection<string>(choices));
        list.SelectedItem = 0;
        list.MouseHighlightStates |= MouseState.In;
        return list;
    }

    static Button CreateButton(string text, bool isDefault, Action action)
    {
        var button = new Button { Text = text, IsDefault = isDefault };
        button.Accepting += (_, _) => action();
        return button;
    }

    static void Layout(Dialog dialog, ListView list, Button ok, Button cancel)
    {
        ok.X = Pos.Center() - 10;
        ok.Y = Pos.Bottom(list);
        cancel.X = Pos.Right(ok) + 2;
        cancel.Y = ok.Y;
        dialog.Add(list, ok, cancel);
    }

    static T RunOnOwner<T>(Func<IApplication, CancellationToken, T> action, CancellationToken cancellationToken)
    {
        var host = TerminalUi.RequireHost();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(host.LifetimeToken, cancellationToken);
        return Environment.CurrentManagedThreadId == host.Application.MainThreadId
            ? action(host.Application, linked.Token)
            : InvokeOnOwner(host.OwnerContext, () => action(host.Application, linked.Token));
    }

    static T InvokeOnOwner<T>(SynchronizationContext context, Func<T> action)
    {
        T result = default!;
        ExceptionDispatchInfo? failure = null;
        context.Send(_ =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        }, null);
        failure?.Throw();
        return result;
    }

    static Task InvokeOnOwnerAsync(SynchronizationContext context, Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Post(async _ =>
        {
            try
            {
                await action().ConfigureAwait(false);
                completion.TrySetResult();
            }
            catch (OperationCanceledException ex)
            {
                completion.TrySetCanceled(ex.CancellationToken);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }, null);
        return completion.Task;
    }

    sealed class DialogProgress(
        IApplication app,
        Func<bool> isRunning,
        Action<DownloadProgress> update) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value)
        {
            if (!isRunning())
                return;

            app.Invoke(() =>
            {
                if (isRunning())
                    update(value);
            });
        }
    }
}
