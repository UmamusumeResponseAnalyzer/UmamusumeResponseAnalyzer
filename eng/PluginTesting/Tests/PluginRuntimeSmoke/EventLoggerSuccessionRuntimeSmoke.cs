using EventLoggerPlugin;
using Gallop;
using System.Drawing;
using System.Globalization;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using UmamusumeResponseAnalyzer.Plugin;
using UmamusumeResponseAnalyzer.TerminalGui;
using TColor = Terminal.Gui.Drawing.Color;

static partial class EventLoggerRuntimeSmoke
{
    const int OverflowDetailCount = 20;
    const int OverflowFactorBaseId = 9_900_000;
    const string SuccessionViewportId = "event-logger-succession";
    static readonly string[] DetailViewportIds =
    [
        "event-logger-succession-hint-0",
        "event-logger-succession-hint-1",
        "event-logger-succession-factor-0",
        "event-logger-succession-factor-1",
    ];
    static void RunSuccessionChoiceDisplay(WorkspaceSmokeSession ui)
    {
        PluginCases.InitializeEventLoggerSuccessionDatabase();
        using var context = new RuntimePluginContext(ui.Application);
        var eventResponse = new EventResponseAnalyzer.EventResponseAnalyzer();
        var eventLogger = new EventLoggerPlugin.EventLoggerPlugin();
        try
        {
            eventResponse.Initialize(context);
            eventLogger.Initialize(context);

            var check = ResponseRegistrations(context, typeof(SingleModeCheckEventResponse));
            var exec = ResponseRegistrations(context, typeof(SingleModeExecCommandResponse));
            var load = ResponseRegistrations(context, typeof(SingleModeLoadResponse));
            RequirePriorities(check, [-1, 2, 3], "check_event analyzer order");
            RequirePriorities(exec, [-1, 3], "exec_command analyzer order");
            RequirePriorities(load, [-1, 2, 3], "load analyzer order");

            var succession = check.Single(registration => registration.Priority == 3);
            ui.Bootstrap.SwitchTo();
            succession.Invoke(new SingleModeCheckEventResponse()).GetAwaiter().GetResult();
            ui.Flush();
            if (!ReferenceEquals(Workspace.Current, ui.Bootstrap) ||
                ui.CaptureScreen().Contains("------ 继承选择 ------", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Incomplete succession data created a panel or changed the foreground workspace.");
            }

            var firstResponse = CreateSuccessionResponse(10101, 20202);
            ui.Bootstrap.SwitchTo();
            foreach (var registration in check)
            {
                registration.Invoke(firstResponse).GetAwaiter().GetResult();
                ui.Flush();
                if (registration.Priority == -1 && !ReferenceEquals(Workspace.Current, ui.Bootstrap))
                {
                    throw new InvalidOperationException(
                        "EventLogger's priority -1 state analyzer still published succession output.");
                }
                if (registration.Priority == 2 && Workspace.Current is not { Title: "EventResponseAnalyzer" })
                {
                    throw new InvalidOperationException(
                        "EventResponseAnalyzer did not publish before EventLogger's succession analyzer.");
                }
            }

            if (Workspace.Current is not { Title: "事件记录" })
            {
                throw new InvalidOperationException(
                    "The priority 3 succession analyzer did not leave EventLogger in the foreground.");
            }

            var screen = ui.CaptureScreen();
            if (!ContainsRenderedLine(screen, "继承结果 10101", "继承结果 20202"))
                throw new InvalidOperationException("Succession candidates were not rendered horizontally.");
            if (screen.Contains("位置", StringComparison.Ordinal))
                throw new InvalidOperationException("Succession details displayed a factor source position.");

            var cells = ui.CaptureCells(180, 80);
            var firstHeader = FindCellToken(cells, "继承结果 10101");
            var secondHeader = FindCellToken(cells, "继承结果 20202");
            if (secondHeader.Points[0].X - firstHeader.Points[0].X != 55 ||
                secondHeader.Points[0].Y != firstHeader.Points[0].Y)
            {
                throw new InvalidOperationException(
                    "Succession candidates did not retain the square-bordered side-by-side 52-column layout.");
            }

            var hintHeader = FindCellToken(cells, "技能Hint详情: 3");
            var factorHeader = FindCellToken(cells, "白因子详情: 2");
            if (hintHeader.Points[0].Y >= factorHeader.Points[0].Y)
                throw new InvalidOperationException("Skill Hint details were not rendered above white factors.");

            FindCellToken(cells, "测试技能 Lv.1 -> Lv.2");
            FindCellToken(cells, "新增技能 Lv.0 -> Lv.1");
            FindCellToken(cells, "未知技能 #30/2 Lv.0 -> Lv.1");
            FindCellToken(cells, "测试白因子★");
            FindCellToken(cells, "未知因子 #1000002 Lv.2");

            var baselineState = InspectSuccessionViewport(ui);
            var baselineHint = baselineState.Detail(DetailViewportIds[0]);
            var baselineFactor = baselineState.Detail(DetailViewportIds[2]);
            var borderColumns = new[] { 0, 55, 110 };
            (int Y, string[] Graphemes)[] expectedBorderRows =
            [
                (1, ["┌", "┬", "┐"]),
                (3, ["├", "┼", "┤"]),
                (baselineHint.Frame.Y - 2, ["├", "┼", "┤"]),
                (baselineFactor.Frame.Y - 2, ["├", "┼", "┤"]),
                (baselineFactor.Frame.Bottom, ["└", "┴", "┘"]),
            ];
            foreach (var row in expectedBorderRows)
            {
                for (var columnIndex = 0; columnIndex < borderColumns.Length; columnIndex++)
                {
                    var point = new Point(
                        baselineState.ScreenFrame.X + borderColumns[columnIndex],
                        baselineState.ScreenFrame.Y + row.Y);
                    var actual = cells[point.Y, point.X].Grapheme;
                    if (actual != row.Graphemes[columnIndex])
                    {
                        throw new InvalidOperationException(
                            $"Succession table border at {point}: " +
                            $"expected '{row.Graphemes[columnIndex]}', got '{actual}'.");
                    }
                }
            }

            RequireForeground(cells, "------ 继承选择 ------", 0, new(StandardColor.BrightGreen), "separator");
            RequireForeground(cells, "继承结果 10101", 5, new(StandardColor.BrightGreen), "lottery id");
            RequireForeground(cells, "属性: 10", 4, new(StandardColor.BrightCyan), "stat gain");
            RequireForeground(cells, "短 适性提升: G -> F", 0, new(StandardColor.BrightYellow), "aptitude gain");
            RequireForeground(cells, "技能Hint详情: 3", 10, new(StandardColor.BrightCyan), "skill Hint count");
            RequireForeground(cells, "测试技能 Lv.1 -> Lv.2", 5, new(StandardColor.BrightCyan), "skill Hint level");
            RequireForeground(cells, "白因子详情: 2", 7, new(StandardColor.BrightCyan), "white factor count");
            RequireForeground(cells, "测试白因子★", 5, new(StandardColor.BrightCyan), "white factor rank");

            VerifySuccessionScrolling(ui, succession);

            ui.Bootstrap.SwitchTo();
            succession.Invoke(CreateSuccessionResponse(30303)).GetAwaiter().GetResult();
            ui.Flush();
            var overwritten = ui.CaptureScreen();
            if (Workspace.Current is not { Title: "事件记录" } ||
                !overwritten.Contains("继承结果 30303", StringComparison.Ordinal) ||
                overwritten.Contains("继承结果 10101", StringComparison.Ordinal) ||
                overwritten.Contains("继承结果 20202", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The succession panel did not overwrite its existing key in place and refocus EventLogger.");
            }
        }
        finally
        {
            eventLogger.DisposeAsync().GetAwaiter().GetResult();
            eventResponse.DisposeAsync().GetAwaiter().GetResult();
        }
    }

    static void VerifySuccessionScrolling(
        WorkspaceSmokeSession ui,
        RecordedAnalyzerRegistration succession)
    {
        VerifyAsymmetricDetailLayout(ui, succession);
        VerifyIndependentDetailScrolling(ui, succession);
        VerifyOuterHorizontalScrolling(ui);
        _ = ui.CaptureScreen(120, 40, restore: false, flushHost: false);
    }

    static void VerifyAsymmetricDetailLayout(
        WorkspaceSmokeSession ui,
        RecordedAnalyzerRegistration succession)
    {
        succession.Invoke(CreateAsymmetricSuccessionResponse(10101, 20202))
            .GetAwaiter().GetResult();
        ui.Flush();
        _ = ui.CaptureScreen(160, 30, restore: false, flushHost: false);

        var state = InspectSuccessionViewport(ui);
        RequireOuterVerticalFixed(state);
        RequireAlignedDetailRegions(state);
        var leftHint = state.Detail(DetailViewportIds[0]);
        var rightHint = state.Detail(DetailViewportIds[1]);
        var leftFactor = state.Detail(DetailViewportIds[2]);
        var rightFactor = state.Detail(DetailViewportIds[3]);
        if (!leftHint.VerticalScrollBarVisible ||
            rightHint.VerticalScrollBarVisible ||
            leftFactor.VerticalScrollBarVisible ||
            !rightFactor.VerticalScrollBarVisible)
        {
            throw new InvalidOperationException(
                "Asymmetric succession details did not retain independent scrollbar visibility.");
        }
        if (leftFactor.Frame.Height != 6 || rightFactor.Frame.Height != 6 ||
            leftHint.MaxY == 0)
        {
            throw new InvalidOperationException(
                "Succession height allocation did not prioritize Hint rows while reserving six factor rows.");
        }
        if (!SendSuccessionWheel(ui, DetailViewportIds[1], MouseFlags.WheeledDown) ||
            !SendSuccessionWheel(ui, DetailViewportIds[2], MouseFlags.WheeledDown))
        {
            throw new InvalidOperationException(
                "A detail region without a vertical scrollbar retained its wheel event.");
        }
        if (SendSuccessionWheel(ui, DetailViewportIds[0], MouseFlags.WheeledUp) ||
            SendSuccessionWheel(ui, DetailViewportIds[3], MouseFlags.WheeledUp))
        {
            throw new InvalidOperationException(
                "A visible detail scrollbar let an upper-bound wheel reach Host.");
        }
    }

    static void VerifyIndependentDetailScrolling(
        WorkspaceSmokeSession ui,
        RecordedAnalyzerRegistration succession)
    {
        succession.Invoke(CreateOverflowSuccessionResponse(10101, 20202))
            .GetAwaiter().GetResult();
        ui.Flush();
        _ = ui.CaptureScreen(160, 30, restore: false, flushHost: false);

        var state = InspectSuccessionViewport(ui);
        RequireOuterVerticalFixed(state);
        RequireAlignedDetailRegions(state);
        if (state.HorizontalScrollBarVisible ||
            state.Details.Any(detail => !detail.VerticalScrollBarVisible))
        {
            throw new InvalidOperationException(
                "The four overflowing succession detail regions did not each show a vertical scrollbar.");
        }

        _ = ui.InvokeOnOwner(() =>
        {
            FindSuccessionView(ui, DetailViewportIds[0]).SetFocus();
            return true;
        });
        ui.SendKey(Key.End);
        var keyboardEnd = InspectSuccessionViewport(ui);
        if (keyboardEnd.Detail(DetailViewportIds[0]).ViewportY !=
                keyboardEnd.Detail(DetailViewportIds[0]).MaxY ||
            keyboardEnd.Details.Skip(1).Any(detail => detail.ViewportY != 0) ||
            keyboardEnd.ViewportY != 0)
        {
            throw new InvalidOperationException(
                "End did not scroll only the focused succession detail region.");
        }
        ui.SendKey(Key.Home);
        if (InspectSuccessionViewport(ui).Details.Any(detail => detail.ViewportY != 0))
            throw new InvalidOperationException("Home did not restore the focused detail region.");

        string[] fixedTokens =
        [
            "继承结果 10101",
            "属性: 10",
            "技能Hint详情: 23",
            "白因子详情: 22",
        ];
        var beforeCells = ui.CaptureCells(160, 30);
        var fixedBefore = fixedTokens
            .Select(token => FindCellToken(beforeCells, token).Points[0])
            .ToArray();

        foreach (var id in DetailViewportIds)
        {
            state = InspectSuccessionViewport(ui);
            if (SendSuccessionWheel(ui, id, MouseFlags.WheeledUp))
            {
                throw new InvalidOperationException(
                    $"Visible detail '{id}' let an upper-bound wheel reach Host.");
            }

            var beforeOffsets = state.Details.ToDictionary(
                detail => detail.Id,
                detail => detail.ViewportY);
            if (SendSuccessionWheel(ui, id, MouseFlags.WheeledDown))
            {
                throw new InvalidOperationException(
                    $"Visible detail '{id}' let a wheel reach Host.");
            }

            var moved = InspectSuccessionViewport(ui);
            if (moved.Detail(id).ViewportY <= beforeOffsets[id] ||
                moved.Details.Any(detail =>
                    detail.Id != id && detail.ViewportY != beforeOffsets[detail.Id]))
            {
                throw new InvalidOperationException(
                    $"Wheel input for '{id}' changed the wrong succession detail region.");
            }

            for (var index = moved.Detail(id).ViewportY;
                 index < moved.Detail(id).MaxY + 2;
                 index++)
            {
                if (SendSuccessionWheel(ui, id, MouseFlags.WheeledDown))
                {
                    throw new InvalidOperationException(
                        $"Visible detail '{id}' let a lower-bound wheel reach Host.");
                }
            }

            var bottom = InspectSuccessionViewport(ui);
            if (bottom.Detail(id).ViewportY != bottom.Detail(id).MaxY)
            {
                throw new InvalidOperationException(
                    $"Detail '{id}' did not reach its native vertical scroll end.");
            }
            if (SendSuccessionWheel(ui, id, MouseFlags.WheeledDown))
            {
                throw new InvalidOperationException(
                    $"Detail '{id}' did not consume a wheel at its lower boundary.");
            }
        }

        state = InspectSuccessionViewport(ui);
        RequireOuterVerticalFixed(state);
        var afterCells = ui.CaptureCells(160, 30);
        var fixedAfter = fixedTokens
            .Select(token => FindCellToken(afterCells, token).Points[0])
            .ToArray();
        if (!fixedBefore.SequenceEqual(fixedAfter))
        {
            throw new InvalidOperationException(
                "Scrolling succession details moved a fixed title, summary, or detail heading.");
        }

        _ = ui.CaptureScreen(160, 80, restore: false, flushHost: false);
        var expanded = InspectSuccessionViewport(ui);
        RequireOuterVerticalFixed(expanded);
        RequireAlignedDetailRegions(expanded);
        if (expanded.Details.Any(detail =>
                detail.VerticalScrollBarVisible || detail.ViewportY != 0))
        {
            throw new InvalidOperationException(
                "Expanding the succession viewport did not hide detail scrollbars and clamp offsets.");
        }
        foreach (var id in DetailViewportIds)
        {
            if (!SendSuccessionWheel(ui, id, MouseFlags.WheeledDown))
            {
                throw new InvalidOperationException(
                    $"Hidden detail scrollbar '{id}' retained a wheel event.");
            }
        }

        _ = ui.CaptureScreen(160, 30, restore: false, flushHost: false);
        var shrunk = InspectSuccessionViewport(ui);
        if (shrunk.Details.Any(detail =>
                !detail.VerticalScrollBarVisible || detail.ViewportY != 0))
        {
            throw new InvalidOperationException(
                "Shrinking the succession viewport did not restore independent detail scrollbars at the top.");
        }
    }

    static void VerifyOuterHorizontalScrolling(WorkspaceSmokeSession ui)
    {
        _ = ui.CaptureScreen(70, 80, restore: false, flushHost: false);
        var state = InspectSuccessionViewport(ui);
        RequireOuterVerticalFixed(state);
        if (!state.HorizontalScrollBarVisible ||
            state.Details.Any(detail => detail.VerticalScrollBarVisible))
        {
            throw new InvalidOperationException(
                "A narrow succession viewport did not show only the outer horizontal scrollbar.");
        }
        if (SendSuccessionWheel(ui, SuccessionViewportId, MouseFlags.WheeledLeft))
        {
            throw new InvalidOperationException(
                "The visible outer horizontal scrollbar let a left-bound wheel reach Host.");
        }

        for (var index = 0; index < state.MaxX + 2; index++)
        {
            if (SendSuccessionWheel(ui, SuccessionViewportId, MouseFlags.WheeledRight))
            {
                throw new InvalidOperationException(
                    "The visible outer horizontal scrollbar let a wheel reach Host.");
            }
        }

        state = InspectSuccessionViewport(ui);
        if (state.ViewportX != state.MaxX || state.MaxX == 0 ||
            state.Details.Any(detail => detail.ViewportY != 0))
        {
            throw new InvalidOperationException(
                "Outer horizontal scrolling moved a detail vertically or did not reach its end.");
        }
        if (SendSuccessionWheel(ui, SuccessionViewportId, MouseFlags.WheeledRight))
        {
            throw new InvalidOperationException(
                "The outer horizontal scrollbar did not consume a wheel at its right boundary.");
        }

        _ = ui.CaptureScreen(160, 80, restore: false, flushHost: false);
        state = InspectSuccessionViewport(ui);
        RequireOuterVerticalFixed(state);
        if (state.HorizontalScrollBarVisible || state.ViewportX != 0)
        {
            throw new InvalidOperationException(
                "Re-expanding the succession viewport did not hide the outer scrollbar and clamp X.");
        }
        if (!SendSuccessionWheel(ui, SuccessionViewportId, MouseFlags.WheeledRight))
        {
            throw new InvalidOperationException(
                "The outer succession viewport retained a horizontal wheel without a scrollbar.");
        }
    }

    static void RequireOuterVerticalFixed(SuccessionViewportState state)
    {
        if (state.VerticalScrollBarVisible || state.ViewportY != 0 || state.MaxY != 0)
        {
            throw new InvalidOperationException(
                "The outer succession viewport acquired vertical scrolling.");
        }
    }

    static void RequireAlignedDetailRegions(SuccessionViewportState state)
    {
        var leftHint = state.Detail(DetailViewportIds[0]);
        var rightHint = state.Detail(DetailViewportIds[1]);
        var leftFactor = state.Detail(DetailViewportIds[2]);
        var rightFactor = state.Detail(DetailViewportIds[3]);
        if (leftHint.Frame.Y != rightHint.Frame.Y ||
            leftHint.Frame.Height != rightHint.Frame.Height ||
            leftFactor.Frame.Y != rightFactor.Frame.Y ||
            leftFactor.Frame.Height != rightFactor.Frame.Height ||
            leftFactor.Frame.Y != leftHint.Frame.Bottom + 2)
        {
            throw new InvalidOperationException(
                "Left and right succession detail boundaries were not aligned.");
        }
        if (leftFactor.Frame.Height < 6)
        {
            throw new InvalidOperationException(
                "A succession factor detail region reserved fewer than six content rows.");
        }
    }

    static SuccessionViewportState InspectSuccessionViewport(WorkspaceSmokeSession ui)
        => ui.InvokeOnOwner(() =>
        {
            var view = FindSuccessionView(ui, SuccessionViewportId);
            var contentSize = view.GetContentSize();
            var details = DetailViewportIds.Select(id =>
            {
                var detail = FindSuccessionView(ui, id);
                var detailContentSize = detail.GetContentSize();
                return new DetailViewportState(
                    id,
                    detail.Frame,
                    detail.VerticalScrollBar.Visible,
                    detail.Viewport.Y,
                    detail.Viewport.Height,
                    detailContentSize.Height,
                    Math.Max(0, detailContentSize.Height - detail.Viewport.Height));
            }).ToArray();
            return new SuccessionViewportState(
                view.VerticalScrollBar.Visible,
                view.HorizontalScrollBar.Visible,
                view.Viewport.X,
                view.Viewport.Y,
                view.Viewport.Width,
                view.Viewport.Height,
                Math.Max(0, contentSize.Width - view.Viewport.Width),
                Math.Max(0, contentSize.Height - view.Viewport.Height),
                view.FrameToScreen(),
                details);
        });

    static bool SendSuccessionWheel(
        WorkspaceSmokeSession ui,
        string targetId,
        MouseFlags flags)
        => ui.InvokeOnOwner(() =>
        {
            var top = ui.Application.TopRunnableView
                ?? throw new InvalidOperationException("Runtime smoke has no top-level view.");
            var view = FindSuccessionView(ui, targetId);
            var frame = view.FrameToScreen();
            if (frame.Width <= 0 || frame.Height <= 0)
                throw new InvalidOperationException($"Succession target '{targetId}' has no viewport.");

            var reachedTop = false;
            void ObserveTop(object? sender, Mouse observed) => reachedTop = true;

            top.MouseEvent += ObserveTop;
            try
            {
                ui.Application.Mouse.RaiseMouseEvent(new()
                {
                    ScreenPosition = new Point(
                        frame.X + Math.Min(2, frame.Width - 1),
                        frame.Y + Math.Min(1, frame.Height - 1)),
                    Flags = flags,
                });
                ui.Application.LayoutAndDraw(forceRedraw: true);
            }
            finally
            {
                top.MouseEvent -= ObserveTop;
            }

            return reachedTop;
        });

    static View FindSuccessionView(WorkspaceSmokeSession ui, string id)
        => Descendants(ui.Application.TopRunnableView
                ?? throw new InvalidOperationException("Runtime smoke has no top-level view."))
            .SingleOrDefault(view => view.Id == id)
            ?? throw new InvalidOperationException($"Published succession view '{id}' was not found.");

    static IEnumerable<View> Descendants(View root)
    {
        yield return root;
        foreach (var child in root.SubViews)
        foreach (var descendant in Descendants(child))
            yield return descendant;
    }

    static RecordedAnalyzerRegistration[] ResponseRegistrations(
        RuntimePluginContext context,
        Type payloadType)
        =>
        [
            .. context.AnalyzerRegistry.Registrations
                .Where(candidate =>
                    candidate.Kind == AnalyzerKind.Response &&
                    candidate.PayloadType == payloadType)
                .OrderBy(candidate => candidate.Priority),
        ];

    static void RequirePriorities(
        IReadOnlyList<RecordedAnalyzerRegistration> registrations,
        int[] expected,
        string name)
    {
        var actual = registrations.Select(registration => registration.Priority).ToArray();
        if (!actual.SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                $"{name}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
        }
    }

    static SingleModeCheckEventResponse CreateOverflowSuccessionResponse(params int[] lotteryIds)
    {
        var response = CreateSuccessionResponse(lotteryIds);
        var choices = response.data!.unchecked_event_array![0]!
            .succession_event_info!.succession_gain_info_array!;
        foreach (var choice in choices)
        {
            AddOverflowHints(choice);
            AddOverflowFactors(choice);
        }

        return response;
    }

    static SingleModeCheckEventResponse CreateAsymmetricSuccessionResponse(params int[] lotteryIds)
    {
        var response = CreateSuccessionResponse(lotteryIds);
        var choices = response.data!.unchecked_event_array![0]!
            .succession_event_info!.succession_gain_info_array!;
        AddOverflowHints(choices[0]);
        AddOverflowFactors(choices[1]);
        return response;
    }

    static void AddOverflowHints(SuccessionGainInfo choice)
        => choice.skill_tips_array =
        [
            .. choice.skill_tips_array,
            .. Enumerable.Range(0, OverflowDetailCount).Select(index => new ExtraSkillTips
            {
                group_id = 1_000 + index,
                rarity = 1,
                level = index % 5 + 1,
            }),
        ];

    static void AddOverflowFactors(SuccessionGainInfo choice)
        => choice.effected_factor_array =
        [
            .. choice.effected_factor_array,
            new()
            {
                position = 2,
                factor_info_array =
                [
                    .. Enumerable.Range(0, OverflowDetailCount).Select(index => new ExtraFactorInfo
                    {
                        factor_id = OverflowFactorBaseId + index,
                        level = 3,
                    }),
                ],
            },
        ];

    static SingleModeCheckEventResponse CreateSuccessionResponse(params int[] lotteryIds)
    {
        var chara = CreateChara(10, charaId: 300, turn: 1, legend: true);
        chara.skill_point = 50;
        chara.skill_tips_array = [new() { group_id = 10, rarity = 1, level = 1 }];
        chara.proper_distance_short = 1;
        chara.proper_distance_mile = 1;
        chara.proper_distance_middle = 1;
        chara.proper_distance_long = 1;
        chara.proper_running_style_nige = 1;
        chara.proper_running_style_oikomi = 1;
        chara.proper_running_style_sashi = 1;
        chara.proper_running_style_senko = 1;
        chara.proper_ground_turf = 1;
        chara.proper_ground_dirt = 1;

        return new()
        {
            data = new()
            {
                chara_info = chara,
                home_info = CreateHomeInfo(legend: true),
                select_index_info_array = [],
                unchecked_event_array =
                [
                    new()
                    {
                        story_id = 400000040,
                        event_contents_info = new() { choice_array = [] },
                        succession_event_info = new()
                        {
                            succession_gain_info_array =
                            [
                                .. lotteryIds.Select((lotteryId, index) =>
                                    CreateSuccessionChoice(lotteryId, (index + 1) * 10)),
                            ],
                        },
                    },
                ],
            },
        };
    }

    static SuccessionGainInfo CreateSuccessionChoice(int lotteryId, int statGain)
        => new()
        {
            lottery_id = lotteryId,
            speed = 100 + statGain,
            stamina = 100,
            power = 100,
            guts = 100,
            wiz = 100,
            skill_point = 55,
            proper_distance_short = 2,
            proper_distance_mile = 1,
            proper_distance_middle = 1,
            proper_distance_long = 1,
            proper_running_style_nige = 1,
            proper_running_style_oikomi = 1,
            proper_running_style_sashi = 1,
            proper_running_style_senko = 1,
            proper_ground_turf = 1,
            proper_ground_dirt = 1,
            effected_factor_array =
            [
                new()
                {
                    position = 1,
                    factor_info_array =
                    [
                        new() { factor_id = 1_000_001, level = 1 },
                        new() { factor_id = 1_000_002, level = 2 },
                        new() { factor_id = 900, level = 1 },
                    ],
                },
            ],
            skill_tips_array =
            [
                new() { group_id = 10, rarity = 1, level = 2 },
                new() { group_id = 20, rarity = 1, level = 1 },
                new() { group_id = 30, rarity = 2, level = 1 },
            ],
        };

    static bool ContainsRenderedLine(string rendered, params string[] expectedParts)
        => rendered
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Any(line => expectedParts.All(part => line.Contains(part, StringComparison.Ordinal)));

    static CellToken FindCellToken(Cell[,] cells, string token)
    {
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(token);
        while (enumerator.MoveNext())
            elements.Add((string)enumerator.Current);

        for (var y = 0; y < cells.GetLength(0); y++)
        {
            for (var x = 0; x < cells.GetLength(1); x++)
            {
                var points = new List<Point>(elements.Count);
                var column = x;
                var matches = true;
                foreach (var element in elements)
                {
                    if (column >= cells.GetLength(1) || cells[y, column].Grapheme != element)
                    {
                        matches = false;
                        break;
                    }

                    points.Add(new(column, y));
                    column += Math.Max(1, element.GetColumns());
                }

                if (matches)
                    return new([.. points]);
            }
        }

        throw new InvalidOperationException($"Expected framebuffer token '{token}' was not found.");
    }

    static void RequireForeground(
        Cell[,] cells,
        string token,
        int graphemeIndex,
        TColor expected,
        string name)
    {
        var point = FindCellToken(cells, token).Points[graphemeIndex];
        var actual = cells[point.Y, point.X].Attribute?.Foreground
            ?? throw new InvalidOperationException($"{name} has no foreground attribute.");
        if (actual != expected)
            throw new InvalidOperationException($"{name}: expected {expected}, got {actual}.");
    }

    sealed record CellToken(Point[] Points);

    readonly record struct DetailViewportState(
        string Id,
        Rectangle Frame,
        bool VerticalScrollBarVisible,
        int ViewportY,
        int ViewportHeight,
        int ContentHeight,
        int MaxY);

    readonly record struct SuccessionViewportState(
        bool VerticalScrollBarVisible,
        bool HorizontalScrollBarVisible,
        int ViewportX,
        int ViewportY,
        int ViewportWidth,
        int ViewportHeight,
        int MaxX,
        int MaxY,
        Rectangle ScreenFrame,
        DetailViewportState[] Details)
    {
        public DetailViewportState Detail(string id)
            => Details.Single(detail => detail.Id == id);
    }
}
