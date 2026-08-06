param(
    [string]$SourcePath,
    [string]$DestinationPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $SourcePath = Join-Path $repositoryRoot "Strings\zh-Hans\Resources.resw"
}
if ([string]::IsNullOrWhiteSpace($DestinationPath)) {
    $DestinationPath = Join-Path $repositoryRoot "Strings\zh-TW\Resources.resw"
}

if (-not ("NativeTraditionalChineseConverter" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class NativeTraditionalChineseConverter
{
    private const uint LCMAP_TRADITIONAL_CHINESE = 0x04000000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int LCMapStringEx(
        string localeName,
        uint mapFlags,
        string source,
        int sourceLength,
        StringBuilder destination,
        int destinationLength,
        IntPtr versionInformation,
        IntPtr reserved,
        IntPtr sortHandle);

    public static string Convert(string source)
    {
        var destination = new StringBuilder(source.Length * 2 + 1);
        int length = LCMapStringEx(
            "zh-TW",
            LCMAP_TRADITIONAL_CHINESE,
            source,
            source.Length,
            destination,
            destination.Capacity,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
        if (length == 0)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        return destination.ToString(0, length);
    }
}
"@
}

$source = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $SourcePath))
$traditional = [NativeTraditionalChineseConverter]::Convert($source)

# Windows converts character forms; these replacements align product terms with
# common Taiwan usage. Keep longer terms before their component words.
$taiwanTerms = [ordered]@{
    "應用程序" = "應用程式"
    "文件夾" = "資料夾"
    "收件箱" = "收件匣"
    "天氣源" = "天氣來源"
    "服務商" = "服務提供者"
    "發件人" = "寄件者"
    "后臺" = "背景"
    "軟件" = "軟體"
    "服務器" = "伺服器"
    "剪貼板" = "剪貼簿"
    "磁盤" = "磁碟"
    "全屏" = "全螢幕"
    "屏幕" = "螢幕"
    "鼠標" = "滑鼠"
    "托盤" = "系統匣"
    "浮窗" = "浮動視窗"
    "窗口" = "視窗"
    "日歷" = "行事曆"
    "日曆" = "行事曆"
    "日程" = "行程"
    "賬戶" = "帳戶"
    "賬號" = "帳號"
    "郵箱" = "信箱"
    "界面" = "介面"
    "設置" = "設定"
    "配置" = "設定"
    "緩存" = "快取"
    "網絡" = "網路"
    "數據" = "資料"
    "默認" = "預設"
    "加載" = "載入"
    "保存" = "儲存"
    "存儲" = "儲存"
    "添加" = "新增"
    "刷新" = "重新整理"
    "搜索" = "搜尋"
    "鏈接" = "連結"
    "連接" = "連結"
    "綁定" = "連結"
    "本地" = "本機"
    "用戶" = "使用者"
    "登錄" = "登入"
    "圖標" = "圖示"
    "顏色" = "色彩"
    "地址" = "位址"
    "信息" = "資訊"
    "短信" = "簡訊"
    "視頻" = "影片"
    "打印" = "列印"
    "卸載" = "解除安裝"
    "導入" = "匯入"
    "導出" = "匯出"
    "質量" = "品質"
    "禁用" = "停用"
    "支持" = "支援"
    "創建" = "建立"
    "在線" = "線上"
    "遠程" = "遠端"
    "當前" = "目前"
    "新建" = "新增"
    "備注" = "備註"
    "重復" = "重複"
    "布局" = "版面配置"
    "字段" = "欄位"
    "文件" = "檔案"
    "設備" = "裝置"
    "站點" = "網站"
    "正文" = "內文"
    "發送" = "傳送"
    "回復" = "回覆"
    "獲取" = "取得"
    "跟蹤" = "追蹤"
    "坐標" = "座標"
    "打開" = "開啟"
    "命令" = "指令"
    "自定義" = "自訂"
    "優化" = "最佳化"
    "這里" = "這裡"
    "關于" = "關於"
    "稍后" = "稍後"
    "大于" = "大於"
    "用于" = "用於"
    "并" = "並"
    "后" = "後"
    "周" = "週"
}

foreach ($entry in $taiwanTerms.GetEnumerator()) {
    $traditional = $traditional.Replace($entry.Key, $entry.Value)
}

# Keep generated resources stable across Git checkouts and avoid carrying the
# trailing spaces present in the framework's ResX template comments.
$traditional = [regex]::Replace($traditional, '(?m)[ \t]+(?=\r?$)', '')

$destinationDirectory = Split-Path -Parent $DestinationPath
[System.IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null
[System.IO.File]::WriteAllText(
    $DestinationPath,
    $traditional,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Generated $DestinationPath"
