using i18n = UmamusumeResponseAnalyzer.Localization.TerminalGui;
using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace UmamusumeResponseAnalyzer.TerminalGui;

static class ModalDialogs
{
    const int PromptHeight = 3;

    static readonly Scheme PromptScheme = new(UraTheme.Dialog)
    {
        Normal = new Terminal.Gui.Drawing.Attribute(UraTheme.Ink, UraTheme.Surface, TextStyle.Bold)
    };

    internal static string PickExecutable(string title, CancellationToken cancellationToken = default)
    {
        var host = TerminalUi.RequireHost();
        if (Environment.CurrentManagedThreadId != host.Application.MainThreadId)
            return InvokeOnOwner(host.OwnerContext, () => PickExecutable(title, cancellationToken));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(host.LifetimeToken, cancellationToken);
        using var picker = new OpenDialog
        {
            Title = title, OpenMode = OpenMode.File, MustExist = true, AllowsMultipleSelection = false,
            AllowedTypes = [new AllowedType(i18n.Dialog_GameExecutable, [".exe"])]
        };
        Run(host.Application, picker, linked.Token);
        return picker.Canceled ? throw new OperationCanceledException(i18n.Dialog_FileSelectionCanceled) : picker.Path;
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
        using var dialog = CreateDialog(i18n.Dialog_Processing);
        var body = new View
        {
            X = 1,
            Y = PromptHeight,
            Width = Dim.Fill(1),
            Height = Dim.Fill(2),
            CanFocus = true,
            ViewportSettings = ViewportSettingsFlags.HasVerticalScrollBar
        };
        body.KeyDown += (_, key) =>
        {
            var offset = key switch
            {
                _ when key == Key.CursorUp => -1,
                _ when key == Key.CursorDown => 1,
                _ when key == Key.PageUp => -body.Viewport.Height,
                _ when key == Key.PageDown => body.Viewport.Height,
                _ when key == Key.Home => -body.GetContentHeight(),
                _ when key == Key.End => body.GetContentHeight(),
                _ => (int?)null
            };
            if (offset is { } rowsToScroll)
                key.Handled = body.ScrollVertical(rowsToScroll) is true;
        };
        var rows = new Dictionary<string, (Label Label, MeterView Meter)>(StringComparer.Ordinal);
        var running = 1;
        var progress = new DialogProgress(
            app,
            () => Volatile.Read(ref running) != 0,
            value =>
        {
            if (!rows.TryGetValue(value.Id, out var row))
            {
                // Each download takes two rows: its description, then its meter.
                var y = rows.Count * 2;
                row = (
                    new Label
                    {
                        X = 0,
                        Y = y,
                        Width = Dim.Fill(),
                        Text = value.Description
                    },
                    new MeterView
                    {
                        X = 0,
                        Y = y + 1,
                        Width = Dim.Fill()
                    });
                rows.Add(value.Id, row);
                body.Add(row.Label, row.Meter);
                body.SetContentHeight(rows.Count * 2);
            }

            row.Label.Text = value.Description;
            row.Meter.Fraction = value.Total <= 0
                ? 0
                : Math.Clamp((float)value.Completed / value.Total, 0, 1);
        });

        var userCancelled = 0;
        var cancel = CreateButton(i18n.Button_Cancel, false, () =>
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
                i18n.Dialog_OperationCanceled,
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
                throw new ArgumentException(i18n.Dialog_SelectionEmpty, nameof(choices));

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
                throw new ArgumentException(i18n.Dialog_MenuEmpty, nameof(choices));

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
                X = 1,
                Y = 0,
                Width = Dim.Fill(1),
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
                : throw new OperationCanceledException(i18n.Dialog_MenuCanceled);
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
                throw new ArgumentException(i18n.Dialog_MultiSelectionEmpty, nameof(choices));

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
            var ok = CreateButton(i18n.Button_OK, isDefault: true, () =>
            {
                accepted = true;
                app.RequestStop(dialog);
            });
            var cancel = CreateButton(i18n.Button_Cancel, isDefault: false, () => app.RequestStop(dialog));
            Layout(dialog, list, ok, cancel);
            list.SetFocus();
            Run(app, dialog, token);
            if (!accepted)
                throw new OperationCanceledException(i18n.Dialog_MultiSelectionCanceled);
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
            var ok = CreateButton(i18n.Button_OK, true, () =>
            {
                if (allowEmpty || !string.IsNullOrWhiteSpace(input.Text))
                {
                    accepted = true;
                    app.RequestStop(dialog);
                }
            });
            var cancel = CreateButton(i18n.Button_Cancel, false, () => app.RequestStop(dialog));
            ok.X = Pos.Center() - 10;
            ok.Y = Pos.Bottom(input) + 1;
            cancel.X = Pos.Right(ok) + 2;
            cancel.Y = ok.Y;
            dialog.Add(input, ok, cancel);
            input.SetFocus();
            Run(app, dialog, token);

            if (!accepted)
                throw new OperationCanceledException(i18n.Dialog_InputCanceled);
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
            var yes = CreateButton(i18n.Button_Yes, defaultValue, () =>
            {
                result = true;
                app.RequestStop(dialog);
            });
            var no = CreateButton(i18n.Button_No, !defaultValue, () =>
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

    internal static (bool Resize, StartupWindowSizeMode Preference) AskStartupWindowSize(
        int columns,
        int rows,
        CancellationToken cancellationToken)
        => RunOnOwner((app, token) =>
        {
            using var dialog = CreateDialog(i18n.Dialog_WindowSizeTitle, height: 10);
            dialog.Width = Dim.Func(_ => Math.Min(64, app.Screen.Width - 2));
            var title = dialog.SubViews.First();
            title.X = 2;
            title.Y = 1;
            title.Width = Dim.Fill(2);
            title.Height = 1;
            var currentSize = new Label
            {
                X = 2, Y = 3, Width = Dim.Fill(2), Height = 1,
                Text = string.Format(i18n.Dialog_WindowSizeCurrent, columns, rows)
            };
            currentSize.SetScheme(new Scheme(UraTheme.Dialog)
            {
                Normal = new Terminal.Gui.Drawing.Attribute(UraTheme.Muted, UraTheme.Surface)
            });
            var question = new Label
            {
                X = 2, Y = 4, Width = Dim.Fill(2), Height = 1,
                Text = string.Format(i18n.Dialog_WindowSizeQuestion, StartupWindowSize.Columns, StartupWindowSize.Rows)
            };
            var result = (Resize: false, Preference: StartupWindowSizeMode.Ask);
            var choices = new[]
            {
                (Text: i18n.Button_Always, Resize: true, Preference: StartupWindowSizeMode.Always),
                (Text: i18n.Button_Never, Resize: false, Preference: StartupWindowSizeMode.Never),
                (Text: i18n.Button_Yes, Resize: true, Preference: StartupWindowSizeMode.Ask),
                (Text: i18n.Button_No, Resize: false, Preference: StartupWindowSizeMode.Ask)
            };
            dialog.Add(currentSize, question);
            foreach (var choice in choices)
            {
                var isDefault = choice is { Resize: true, Preference: StartupWindowSizeMode.Ask };
                var button = CreateButton(choice.Text, isDefault, () =>
                {
                    result = (choice.Resize, choice.Preference);
                    app.RequestStop(dialog);
                });
                button.X = Pos.Align(Alignment.Center, AlignmentModes.AddSpaceBetweenItems);
                button.Y = 6;
                dialog.Add(button);
            }
            dialog.SubViews.OfType<Button>().Single(button => button.IsDefault).SetFocus();
            Run(app, dialog, token);
            return result;
        }, cancellationToken);

    internal static bool Acknowledge(
        string? title = null,
        CancellationToken cancellationToken = default)
        => RunOnOwner((app, token) =>
        {
            using var dialog = CreateDialog(title ?? i18n.Dialog_ReturnPrompt, height: 9);
            var accepted = false;
            var ok = CreateButton(i18n.Button_OK, true, () =>
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
        var ok = CreateButton(i18n.Button_OK, true, () =>
        {
            accepted = true;
            app.RequestStop(dialog);
        });
        var cancel = CreateButton(i18n.Button_Cancel, false, () => app.RequestStop(dialog));
        list.Accepted += (_, _) =>
        {
            accepted = true;
            app.RequestStop(dialog);
        };
        Layout(dialog, list, ok, cancel);
        list.SetFocus();
        Run(app, dialog, cancellationToken);
        if (!accepted)
            throw new OperationCanceledException(i18n.Dialog_SelectionCanceled);
        return list.SelectedItem ?? 0;
    }

    static void Run(
        IApplication app,
        IRunnable runnable,
        CancellationToken cancellationToken)
    {
        if (Environment.CurrentManagedThreadId != app.MainThreadId)
            throw new InvalidOperationException(i18n.Dialog_RequiresOwnerThread);

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
            Width = Dim.Percent(80),
            Height = height is null ? Dim.Percent(80) : height.Value
        };
        UraTheme.UseRuleBorder(dialog);
        var prompt = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = PromptHeight,
            Text = title
        };
        prompt.SetScheme(PromptScheme);
        dialog.Add(prompt);
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
        var prompt = dialog.SubViews.First();
        prompt.Height = Dim.Auto(DimAutoStyle.Text);
        list.Y = Pos.Bottom(prompt);
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

    /// <summary>A one-row meter: accent dashes for the done part, rule dashes for the rest, then the percentage.</summary>
    sealed class MeterView : View
    {
        float fraction;

        internal MeterView()
        {
            Height = 1;
            CanFocus = false;
        }

        internal float Fraction
        {
            get => fraction;
            set
            {
                if (fraction == value)
                    return;
                fraction = value;
                SetNeedsDraw();
            }
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var background = GetAttributeForRole(VisualRole.Normal).Background;
            var percent = $"{(int)Math.Round(fraction * 100)}%";
            var track = Math.Max(0, Viewport.Width - 5);
            var done = (int)Math.Round(track * fraction);
            SetAttribute(new Terminal.Gui.Drawing.Attribute(UraTheme.Accent, background));
            AddStr(0, 0, new string('━', done));
            SetAttribute(new Terminal.Gui.Drawing.Attribute(UraTheme.Rule, background));
            AddStr(done, 0, new string('━', track - done));
            SetAttribute(new Terminal.Gui.Drawing.Attribute(UraTheme.Muted, background));
            AddStr(Math.Max(0, Viewport.Width - percent.Length), 0, percent);
            return true;
        }
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
