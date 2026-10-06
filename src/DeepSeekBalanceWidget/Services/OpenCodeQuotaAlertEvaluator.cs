using System;
using System.Collections.Generic;
using System.Linq;
using DeepSeekBalanceWidget.Models;

namespace DeepSeekBalanceWidget.Services;

/// <summary>
/// OpenCode Go 额度预警判定：与 ChatGPT 额度预警共用同一套档位配置，
/// 按「窗口类型」（5h / 周 / 月）独立跟踪剩余百分比。
/// 判定规则（2026-08-31 调整）：
/// - 首次观察只建立基线，避免应用启动即弹历史遗留预警；
/// - 不做恢复播报（用户要求：OpenCode 消耗极少，恢复提醒意义不大）；
/// - 低量预警靠档位记录去重，进入新周期（ResetsAt 前进）时清空记录重新武装。
/// 平台无关，Windows 与 macOS 共用同一份判定逻辑。
/// </summary>
public sealed class OpenCodeQuotaAlertEvaluator
{
    private readonly Dictionary<string, CodexQuotaWindowState> _states = new();

    /// <summary>评估 OpenCode 三个额度窗口，返回本次需要提醒的事件（可能为空）。</summary>
    public IReadOnlyList<OpenCodeQuotaAlert> Evaluate(
        OpenCodeUsageSnapshot? snapshot,
        AppConfig cfg,
        DateTimeOffset now)
    {
        var alerts = new List<OpenCodeQuotaAlert>();
        if (!cfg.EnableCodexQuotaAlerts || snapshot is not { IsAvailable: true }) return alerts;

        var thresholds = NormalizeThresholds(cfg.CodexQuotaAlertThresholds);
        if (thresholds.Count == 0) return alerts;

        foreach (var window in OrderWindows(snapshot.Windows))
        {
            var state = GetOrCreateState(window.Kind);

            // 首次观察只建立基线，不打扰。
            if (state.LastRemainingPercent is null)
            {
                state.LastRemainingPercent = window.RemainingPercent;
                state.LastResetsAt = window.ResetsAt;
                continue;
            }

            // 进入新周期（ResetsAt 严格前进超过 30 分钟）时清空已通知档位，
            // 使下一周期的低量预警可以重新触发；OpenCode 本身不播报恢复。
            bool isNewCycle = window.ResetsAt.HasValue
                              && state.LastResetsAt.HasValue
                              && window.ResetsAt.Value - state.LastResetsAt.Value > CodexQuotaAlertEvaluator.NewCycleMinAdvance;
            if (isNewCycle) state.ResetCycle();

            // 低量预警：一次刷新只播报跨过的最低档位，档位记录去重直到恢复。
            var crossed = thresholds.Where(t => window.RemainingPercent <= t).ToList();
            if (crossed.Count > 0)
            {
                int lowest = crossed.Min();
                if (!state.NotifiedThresholds.Contains(lowest))
                {
                    alerts.Add(new OpenCodeQuotaAlert(
                        window.Kind, OpenCodeUsageFormatter.LabelOf(window.Kind),
                        window.RemainingPercent, lowest, IsRecovery: false, window.ResetsAt,
                        OpenCodeUsageFormatter.EstimateUsedUsd(window)));
                    foreach (int t in crossed) state.NotifiedThresholds.Add(t);
                }
            }

            state.LastRemainingPercent = window.RemainingPercent;
            state.LastResetsAt = window.ResetsAt;
        }

        return alerts;
    }

    private CodexQuotaWindowState GetOrCreateState(string kind)
    {
        if (!_states.TryGetValue(kind, out var state))
        {
            state = new CodexQuotaWindowState();
            _states[kind] = state;
        }
        return state;
    }

    private static IReadOnlyList<OpenCodeUsageWindow> OrderWindows(IReadOnlyList<OpenCodeUsageWindow> windows)
        => windows
            .OrderBy(window => KindOrder(window.Kind))
            .ToArray();

    private static int KindOrder(string kind) => kind switch
    {
        OpenCodeUsageProvider.RollingKind => 0,
        OpenCodeUsageProvider.WeeklyKind => 1,
        OpenCodeUsageProvider.MonthlyKind => 2,
        _ => 3
    };

    /// <summary>清洗阈值档位：只保留 1-99，去重后降序（与 GPT 预警共用同一配置）。</summary>
    private static List<int> NormalizeThresholds(IEnumerable<int>? raw)
        => (raw ?? Enumerable.Empty<int>())
            .Where(t => t > 0 && t < 100)
            .Distinct()
            .OrderByDescending(t => t)
            .ToList();
}
