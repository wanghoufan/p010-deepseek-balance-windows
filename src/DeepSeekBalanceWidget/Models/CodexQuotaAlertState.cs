using System;
using System.Collections.Generic;

namespace DeepSeekBalanceWidget.Models;

/// <summary>
/// 单个「账号 + 额度窗口」的预警跟踪状态。
/// 5 小时窗口与周窗口各自独立跟踪，互不影响。
/// </summary>
public sealed class CodexQuotaWindowState
{
    /// <summary>上次观察到的剩余百分比；null 表示尚无基线（首次观察）。</summary>
    public int? LastRemainingPercent { get; set; }

    /// <summary>上次观察到的重置时间，用于识别新周期。</summary>
    public DateTimeOffset? LastResetsAt { get; set; }

    /// <summary>本周期内已经提醒过的阈值档位，避免同一档位重复打扰。</summary>
    public HashSet<int> NotifiedThresholds { get; } = new();

    /// <summary>上次播报「额度已恢复」的时间，用于吸收阈值附近抖动的重复播报。</summary>
    public DateTimeOffset? LastRecoveryAlertUtc { get; set; }

    /// <summary>
    /// 已播报过「恢复」的那个周期的 ResetsAt 值：同一个重置时间只播报一次恢复，
    /// 防止 ResetsAt 抖动或同类型多窗口拉扯导致恢复提醒反复弹出。
    /// </summary>
    public DateTimeOffset? RecoveryAnnouncedResetsAt { get; set; }

    /// <summary>进入新周期：清空已提醒档位，使下一周期可以重新提醒。</summary>
    public void ResetCycle() => NotifiedThresholds.Clear();
}

/// <summary>
/// 一次额度预警事件（低量预警或额度恢复）。
/// </summary>
public sealed record CodexQuotaAlert(
    string AccountId,
    string Email,
    /// <summary>窗口类型标识：5h / weekly。</summary>
    string WindowKind,
    /// <summary>窗口显示名：5 小时额度 / 周额度。</summary>
    string WindowLabel,
    int RemainingPercent,
    /// <summary>命中的预警档位；恢复提醒为 null。</summary>
    int? Threshold,
    bool IsRecovery,
    DateTimeOffset? ResetsAt);
