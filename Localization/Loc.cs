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
        ["Menu.Appearance"] = "Appearance ▾",
        ["Menu.AppearanceTip"] = "Language and theme",
        ["Menu.Language"] = "Language",
        ["Menu.Theme"] = "Theme",
        ["Menu.Lang.Zh"] = "中文",
        ["Menu.Lang.En"] = "English",
        ["Menu.Theme.Dark"] = "Dark",
        ["Menu.Theme.Light"] = "Light",
        ["Menu.Theme.Custom"] = "Custom colors…",
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
        ["Map.SourceTip"] = "Map tile source (Ovi-style XYZ)",
        ["Map.CustomUrl"] = "Custom XYZ URL",
        ["Map.CustomUrlPrompt"] = "XYZ template with {z}/{x}/{y}:",
        ["Map.Ok"] = "OK",
        ["Map.Cancel"] = "Cancel",
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
        ["Menu.Appearance"] = "外观 ▾",
        ["Menu.AppearanceTip"] = "语言与主题",
        ["Menu.Language"] = "语言",
        ["Menu.Theme"] = "主题",
        ["Menu.Lang.Zh"] = "中文",
        ["Menu.Lang.En"] = "English",
        ["Menu.Theme.Dark"] = "暗黑",
        ["Menu.Theme.Light"] = "明亮",
        ["Menu.Theme.Custom"] = "自定义配色…",
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
        ["Map.SourceTip"] = "地图瓦片图源（奥维风格 XYZ）",
        ["Map.CustomUrl"] = "自定义 XYZ 地址",
        ["Map.CustomUrlPrompt"] = "含 {z}/{x}/{y} 的 XYZ 模板：",
        ["Map.Ok"] = "确定",
        ["Map.Cancel"] = "取消",
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
