using System.ComponentModel;
using System.Globalization;

namespace Chassis_Master_Test_Suite.Localization;

/// <summary>
/// Runtime i18n: zh-CN / en dictionaries. Raise <see cref="LanguageChanged"/> after switch
/// so code-behind can refresh Content/ToolTip that are not DynamicResource-bound.
/// </summary>
public static class Loc
{
    public const string ZhCn = "zh-CN";
    public const string En = "en";

    private static readonly Dictionary<string, Dictionary<string, string>> Tables =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [En] = BuildEn(),
            [ZhCn] = BuildZh()
        };

    private static string _language = ZhCn;

    public static string Language
    {
        get => _language;
        private set => _language = value;
    }

    public static event EventHandler? LanguageChanged;

    public static void SetLanguage(string language)
    {
        var norm = Normalize(language);
        if (string.Equals(_language, norm, StringComparison.OrdinalIgnoreCase))
            return;
        Language = norm;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(norm);
        }
        catch
        {
            // ignore invalid culture on exotic hosts
        }

        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return ZhCn;
        if (language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            return En;
        return ZhCn;
    }

    public static string T(string key)
    {
        if (Tables.TryGetValue(_language, out var table) &&
            table.TryGetValue(key, out var value))
            return value;

        if (Tables[En].TryGetValue(key, out var en))
            return en;

        return key;
    }

    public static string T(string key, params object[] args)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, T(key), args);
        }
        catch
        {
            return T(key);
        }
    }

    private static Dictionary<string, string> BuildEn() => new(StringComparer.Ordinal)
    {
        ["App.Title"] = "Chassis Master Test Suite",
        ["Menu.Layout"] = "Layout ▾",
        ["Menu.LayoutTip"] = "Show or hide workspace panels",
        ["Menu.Language"] = "Language ▾",
        ["Menu.LanguageTip"] = "Interface language",
        ["Menu.Theme"] = "Theme ▾",
        ["Menu.ThemeTip"] = "Light / Dark / Custom",
        ["Menu.ThemeToggleTip"] = "Switch Light / Dark",
        ["Menu.ThemeMore"] = "More ▾",
        ["Menu.ThemeMoreTip"] = "Other themes",
        ["Menu.Lang.Zh"] = "中文",
        ["Menu.Lang.En"] = "English",
        ["Menu.Theme.Dark"] = "Dark",
        ["Menu.Theme.Light"] = "Light",
        ["Menu.Theme.Custom"] = "Custom…",
        ["Menu.Theme.CustomTip"] = "Custom theme colors (placeholder)",
        ["Layout.Dashboard"] = "Dashboard",
        ["Layout.TestResults"] = "Test Results",
        ["Layout.Map"] = "Map",
        ["Layout.Chart"] = "Chart",
        ["Layout.Reset"] = "Reset",
        ["Layout.ToggleDashboard"] = "Toggle Dashboard",
        ["Layout.ToggleTestResults"] = "Toggle Test Results",
        ["Layout.ToggleMap"] = "Toggle Map",
        ["Layout.ToggleChart"] = "Toggle Chart",
        ["Layout.ResetTip"] = "Reset layout to default sizes and visibility",
        ["Toolbar.Load"] = "Load",
        ["Toolbar.LoadTip"] = "Open VBO file(s)",
        ["Toolbar.Online"] = "Online",
        ["Toolbar.OnlineTip"] = "Remote live data (UDP / GSpot / Simulator)",
        ["Toolbar.Offline"] = "Offline",
        ["Toolbar.OfflineTip"] = "VBO replay and multi-file compare",
        ["Toolbar.Clear"] = "Clear",
        ["Toolbar.ClearTip"] = "Close all opened VBO files",
        ["Toolbar.Udp"] = "UDP",
        ["Toolbar.UdpTip"] = "Local UDP / Simulator data source",
        ["Toolbar.GSpot"] = "GSpot…",
        ["Toolbar.GSpotTip"] = "GSpot WebSocket (room + password)",
        ["Toolbar.Simulator"] = "Simulator",
        ["Toolbar.Stop"] = "Stop",
        ["Toolbar.StopTip"] = "Stop the current online data source",
        ["Toolbar.RecordStart"] = "Start",
        ["Toolbar.RecordStop"] = "■ Stop",
        ["Toolbar.Elapsed"] = "Elapsed",
        ["Status.Offline"] = "Offline",
        ["Status.Online"] = "Online",
        ["Status.ConnectionTip"] = "Data source connection status",
        ["Map.Title"] = "Track Map",
        ["Map.Waiting"] = "Waiting for GPS data",
        ["Map.AddGate"] = "Add Gate",
        ["Map.AddGateTip"] = "Click track to place a new named gate",
        ["Map.SelectGate"] = "Select gate",
        ["Map.Width"] = "W",
        ["Map.WidthTip"] = "Gate width (meters)",
        ["Map.Rename"] = "Rename",
        ["Map.RenameTip"] = "Rename selected gate",
        ["Map.Delete"] = "Delete",
        ["Map.DeleteTip"] = "Delete selected gate",
        ["Map.DeleteAll"] = "Delete All",
        ["Map.DeleteAllTip"] = "Remove every gate",
        ["Map.Import"] = "Import",
        ["Map.ImportTip"] = "Import gates from VBTS (.vbts)",
        ["Map.Export"] = "Export",
        ["Map.ExportTip"] = "Export gates",
        ["Map.Gates"] = "Gates",
        ["Map.Basemap"] = "Basemap",
        ["Map.BasemapTip"] = "Toggle online map tiles under the track",
        ["Map.Source"] = "Source",
        ["Map.SourceTip"] = "Satellite basemap (Esri / Google / Bing / Custom XYZ)",
        ["Map.CustomUrl"] = "Custom XYZ URL",
        ["Map.CustomUrlPrompt"] = "XYZ template with {z}/{x}/{y} (optional {s}/{tk}). 世纪空间/21at: paste their tile URL + key (no public free endpoint):",
        ["Map.TiandituKey"] = "Tianditu API key",
        ["Map.TiandituKeyPrompt"] = "Enter your Tianditu (天地图) tk key (console.tianditu.gov.cn):",
        ["Map.Ok"] = "OK",
        ["Map.Cancel"] = "Cancel",
        ["Menu.LangToggleTip"] = "Switch Chinese / English",
        ["Menu.LangMoreTip"] = "Other languages",
        ["Menu.Lang.ShortZh"] = "中",
        ["Menu.Lang.ShortEn"] = "EN",
        ["Menu.Lang.MorePlaceholder"] = "More languages…",
        ["Dashboard.Title"] = "Dashboard",
        ["Dashboard.AddGauge"] = "Add Gauge",
        ["Dashboard.AddGaugeTip"] = "Add a Default numeric gauge",
        ["Dashboard.Reset"] = "Reset",
        ["Dashboard.ResetTip"] = "Reset gauges to default layout",
        ["Dashboard.Cursor"] = "Cursor",
        ["Results.Title"] = "Test Results",
        ["Results.HideConditions"] = "Hide conditions",
        ["Results.ShowConditions"] = "Show conditions",
        ["Results.ToggleConditionsTip"] = "Collapse / expand the conditions area above the results table",
        ["Results.Session"] = "Session",
        ["Results.SessionTip"] = "Edit session metadata written to VBO [session data]",
        ["Results.Type"] = "Type",
        ["Results.Channel"] = "Channel",
        ["Results.Accel"] = "Accel",
        ["Results.Decel"] = "Decel",
        ["Results.Custom"] = "Custom",
        ["Results.Gate"] = "Gate",
        ["Results.StartConditions"] = "Start Condition(s)",
        ["Results.EndConditions"] = "End Condition(s)",
        ["Results.PassConditions"] = "Pass Condition(s)",
        ["Results.StartWhen"] = "Start When",
        ["Results.EndWhen"] = "End When",
        ["Results.AddPass"] = "+ Add Pass",
        ["Results.AddPassTip"] = "Add Pass Condition: Channel Min–Max At gate",
        ["Results.Min"] = "Min",
        ["Results.Max"] = "Max",
        ["Results.At"] = "At",
        ["Results.Remove"] = "Remove",
        ["Results.Start"] = "Start",
        ["Results.End"] = "End",
        ["Results.Compute"] = "Compute",
        ["Results.ComputeTip"] = "Compute across ALL open VBO files / live history",
        ["Results.ExportCsv"] = "Export CSV",
        ["Results.ExportCsvTip"] = "Export current results to CSV",
        ["Results.MathsChannels"] = "Maths Channels...",
        ["Results.MathsChannelsTip"] = "Define calculated channels (VBTS-style editor)",
        ["Results.StatusHint"] = "Compute scans every open VBO. Check rows to overlay-compare on charts.",
        ["Results.FooterHint"] = "Multi-file Compute + checkbox compare (X = seconds from each run start). Single row focus keeps span annotation.",
        ["Results.Col.Source"] = "Source",
        ["Results.Col.Start"] = "Start",
        ["Results.Col.End"] = "End",
        ["Results.Col.Duration"] = "Duration s",
        ["Results.Col.DeltaV"] = "dV km/h",
        ["Results.Col.Dist"] = "Dist m",
        ["Results.Col.Pass"] = "Pass",
        ["Results.Col.PassValues"] = "Pass values",
        ["Results.SelectAllTip"] = "Select all / clear",
        ["Results.SplitterTip"] = "Drag to resize conditions / results",
        ["Curves.Title"] = "Test Data Curves",
        ["Curves.XAxis"] = "X Axis",
        ["Curves.XAutoScale"] = "X Auto Scale",
        ["Curves.AddPlot"] = "+ Add Plot",
        ["Curves.ResetView"] = "Reset View",
        ["Curves.Files"] = "Files",
        ["Curves.PlotName"] = "Plot Name",
        ["Curves.AutoY"] = "Auto Y",
        ["Curves.AddChannel"] = "+ Channel",
        ["Curves.RemovePlot"] = "Remove Plot",
        ["Toolbar.RecordPause"] = "Pause",
        ["Toolbar.RecordResume"] = "Resume",
        ["Status.OfflineFiles"] = "Offline · {0} file(s)",
        ["Status.OfflineTip"] = "Offline VBO replay",
        ["Status.Stopped"] = "Stopped",
        ["Status.StoppedTip"] = "Online connection stopped",
        ["Status.Connecting"] = "Connecting…",
        ["Status.Reconnecting"] = "Reconnecting…",
        ["Status.Faulted"] = "Faulted",
        ["Replay.Title"] = "▷  Replay",
        ["Replay.Hint"] = "Load a recorded VBO file and analyse it offline.",
        ["Replay.OpenVbo"] = "📂  Open VBO…",
        ["Replay.NoFile"] = "No file loaded",
        ["Theme.CustomTitle"] = "Custom theme colors",
        ["Theme.Accent"] = "Accent",
        ["Theme.AppBackground"] = "App background",
        ["Theme.PanelBackground"] = "Panel background",
        ["Theme.Apply"] = "Apply",
        ["Theme.Cancel"] = "Cancel",
        ["Theme.ResetDefaults"] = "Reset to dark defaults",
    };

    private static Dictionary<string, string> BuildZh() => new(StringComparer.Ordinal)
    {
        ["App.Title"] = "Chassis Master Test Suite",
        ["Menu.Layout"] = "布局 ▾",
        ["Menu.LayoutTip"] = "显示或隐藏工作区面板",
        ["Menu.Language"] = "语言 ▾",
        ["Menu.LanguageTip"] = "界面语言",
        ["Menu.Theme"] = "主题 ▾",
        ["Menu.ThemeTip"] = "明亮 / 暗黑 / 自定义",
        ["Menu.ThemeToggleTip"] = "切换明亮 / 暗黑",
        ["Menu.ThemeMore"] = "其它 ▾",
        ["Menu.ThemeMoreTip"] = "其它主题",
        ["Menu.Lang.Zh"] = "中文",
        ["Menu.Lang.En"] = "English",
        ["Menu.Theme.Dark"] = "暗",
        ["Menu.Theme.Light"] = "明",
        ["Menu.Theme.Custom"] = "自定义…",
        ["Menu.Theme.CustomTip"] = "自定义主题配色（占位）",
        ["Layout.Dashboard"] = "仪表盘",
        ["Layout.TestResults"] = "试验结果",
        ["Layout.Map"] = "地图",
        ["Layout.Chart"] = "曲线",
        ["Layout.Reset"] = "重置",
        ["Layout.ToggleDashboard"] = "开关仪表盘",
        ["Layout.ToggleTestResults"] = "开关试验结果",
        ["Layout.ToggleMap"] = "开关地图",
        ["Layout.ToggleChart"] = "开关曲线",
        ["Layout.ResetTip"] = "恢复默认布局尺寸与可见性",
        ["Toolbar.Load"] = "加载",
        ["Toolbar.LoadTip"] = "打开 VBO 文件",
        ["Toolbar.Online"] = "在线",
        ["Toolbar.OnlineTip"] = "远程实时数据（UDP / GSpot / 模拟器）",
        ["Toolbar.Offline"] = "离线",
        ["Toolbar.OfflineTip"] = "VBO 回放与多文件对比",
        ["Toolbar.Clear"] = "清除",
        ["Toolbar.ClearTip"] = "关闭全部已打开的 VBO",
        ["Toolbar.Udp"] = "UDP",
        ["Toolbar.UdpTip"] = "本地 UDP / 模拟器数据源",
        ["Toolbar.GSpot"] = "GSpot…",
        ["Toolbar.GSpotTip"] = "GSpot WebSocket（房间 + 密码）",
        ["Toolbar.Simulator"] = "模拟器",
        ["Toolbar.Stop"] = "停止",
        ["Toolbar.StopTip"] = "停止当前在线数据源",
        ["Toolbar.RecordStart"] = "开始",
        ["Toolbar.RecordStop"] = "■ 停止",
        ["Toolbar.Elapsed"] = "已录",
        ["Status.Offline"] = "离线",
        ["Status.Online"] = "在线",
        ["Status.ConnectionTip"] = "数据源连接状态",
        ["Map.Title"] = "轨迹地图",
        ["Map.Waiting"] = "等待 GPS 数据",
        ["Map.AddGate"] = "添加门",
        ["Map.AddGateTip"] = "点击轨迹放置新门",
        ["Map.SelectGate"] = "选择门",
        ["Map.Width"] = "宽",
        ["Map.WidthTip"] = "门宽（米）",
        ["Map.Rename"] = "重命名",
        ["Map.RenameTip"] = "重命名选中门",
        ["Map.Delete"] = "删除",
        ["Map.DeleteTip"] = "删除选中门",
        ["Map.DeleteAll"] = "全删",
        ["Map.DeleteAllTip"] = "删除全部门",
        ["Map.Import"] = "导入",
        ["Map.ImportTip"] = "从 VBTS (.vbts) 导入门",
        ["Map.Export"] = "导出",
        ["Map.ExportTip"] = "导出门",
        ["Map.Gates"] = "门",
        ["Map.Basemap"] = "底图",
        ["Map.BasemapTip"] = "在轨迹下方开关在线地图瓦片",
        ["Map.Source"] = "图源",
        ["Map.SourceTip"] = "卫星底图（Esri / Google / Bing / 自定义 XYZ）",
        ["Map.CustomUrl"] = "自定义 XYZ 地址",
        ["Map.CustomUrlPrompt"] = "含 {z}/{x}/{y} 的 XYZ 地址（可选 {s}/{tk}）。世纪空间等需自备瓦片地址与密钥（无公开免费源）：",
        ["Map.TiandituKey"] = "天地图密钥",
        ["Map.TiandituKeyPrompt"] = "请输入天地图 tk 密钥（console.tianditu.gov.cn 申请）：",
        ["Map.Ok"] = "确定",
        ["Map.Cancel"] = "取消",
        ["Menu.LangToggleTip"] = "切换中文 / 英文",
        ["Menu.LangMoreTip"] = "其它语言",
        ["Menu.Lang.ShortZh"] = "中",
        ["Menu.Lang.ShortEn"] = "EN",
        ["Menu.Lang.MorePlaceholder"] = "更多语言…",
        ["Dashboard.Title"] = "仪表盘",
        ["Dashboard.AddGauge"] = "添加仪表",
        ["Dashboard.AddGaugeTip"] = "添加默认数值仪表",
        ["Dashboard.Reset"] = "重置",
        ["Dashboard.ResetTip"] = "恢复默认仪表布局",
        ["Dashboard.Cursor"] = "光标",
        ["Results.Title"] = "试验结果",
        ["Results.HideConditions"] = "隐藏条件",
        ["Results.ShowConditions"] = "显示条件",
        ["Results.ToggleConditionsTip"] = "折叠 / 展开结果表上方的条件区",
        ["Results.Session"] = "会话",
        ["Results.SessionTip"] = "编辑写入 VBO [SessionData] 的会话元数据",
        ["Results.Type"] = "类型",
        ["Results.Channel"] = "通道",
        ["Results.Accel"] = "加速",
        ["Results.Decel"] = "减速",
        ["Results.Custom"] = "自定义",
        ["Results.Gate"] = "门",
        ["Results.StartConditions"] = "起始条件",
        ["Results.EndConditions"] = "结束条件",
        ["Results.PassConditions"] = "通过条件",
        ["Results.StartWhen"] = "起始于",
        ["Results.EndWhen"] = "结束于",
        ["Results.AddPass"] = "+ 添加通过条件",
        ["Results.AddPassTip"] = "添加通过条件：通道 Min–Max 在门处",
        ["Results.Min"] = "Min",
        ["Results.Max"] = "Max",
        ["Results.At"] = "于",
        ["Results.Remove"] = "移除",
        ["Results.Start"] = "起始",
        ["Results.End"] = "结束",
        ["Results.Compute"] = "计算",
        ["Results.ComputeTip"] = "对全部已打开 VBO / 实时历史计算",
        ["Results.ExportCsv"] = "导出 CSV",
        ["Results.ExportCsvTip"] = "将当前结果导出为 CSV",
        ["Results.MathsChannels"] = "数学通道...",
        ["Results.MathsChannelsTip"] = "定义计算通道（VBTS 风格编辑器）",
        ["Results.StatusHint"] = "计算会扫描全部已打开 VBO。勾选行可在曲线上叠加对比。",
        ["Results.FooterHint"] = "多文件计算 + 勾选对比（X = 各次从起点起的秒）。单行聚焦保留跨度标注。",
        ["Results.Col.Source"] = "来源",
        ["Results.Col.Start"] = "起始",
        ["Results.Col.End"] = "结束",
        ["Results.Col.Duration"] = "时长 s",
        ["Results.Col.DeltaV"] = "ΔV km/h",
        ["Results.Col.Dist"] = "距离 m",
        ["Results.Col.Pass"] = "通过",
        ["Results.Col.PassValues"] = "通过值",
        ["Results.SelectAllTip"] = "全选 / 清除",
        ["Results.SplitterTip"] = "拖动调整条件区 / 结果区高度",
        ["Curves.Title"] = "试验数据曲线",
        ["Curves.XAxis"] = "X 轴",
        ["Curves.XAutoScale"] = "X 自动缩放",
        ["Curves.AddPlot"] = "+ 添加曲线",
        ["Curves.ResetView"] = "重置视图",
        ["Curves.Files"] = "文件",
        ["Curves.PlotName"] = "曲线名",
        ["Curves.AutoY"] = "Y 自动",
        ["Curves.AddChannel"] = "+ 通道",
        ["Curves.RemovePlot"] = "移除曲线",
        ["Toolbar.RecordPause"] = "暂停",
        ["Toolbar.RecordResume"] = "继续",
        ["Status.OfflineFiles"] = "离线 · {0} 个文件",
        ["Status.OfflineTip"] = "离线 VBO 回放",
        ["Status.Stopped"] = "已停止",
        ["Status.StoppedTip"] = "在线连接已停止",
        ["Status.Connecting"] = "连接中…",
        ["Status.Reconnecting"] = "重连中…",
        ["Status.Faulted"] = "故障",
        ["Replay.Title"] = "▷  回放",
        ["Replay.Hint"] = "加载已录制的 VBO 文件并离线分析。",
        ["Replay.OpenVbo"] = "📂  打开 VBO…",
        ["Replay.NoFile"] = "未加载文件",
        ["Theme.CustomTitle"] = "自定义主题配色",
        ["Theme.Accent"] = "强调色",
        ["Theme.AppBackground"] = "窗口背景",
        ["Theme.PanelBackground"] = "面板背景",
        ["Theme.Apply"] = "应用",
        ["Theme.Cancel"] = "取消",
        ["Theme.ResetDefaults"] = "恢复暗黑默认",
    };
}

/// <summary>Simple INPC wrapper so XAML can bind via ObjectDataProvider if needed.</summary>
public sealed class LocBinder : INotifyPropertyChanged
{
    public static LocBinder Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private LocBinder()
    {
        Loc.LanguageChanged += (_, _) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public string this[string key] => Loc.T(key);
}
