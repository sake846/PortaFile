using System;
using System.Globalization;

namespace PortaFile.Services
{
    public enum UiLanguage
    {
        Japanese,
        English
    }

    public sealed class AppLocalization
    {
        private readonly UiLanguage _language;

        public AppLocalization(ApplicationLastState state)
        {
            _language = ResolveLanguage(state.UiLanguage);
        }

        public static UiLanguage ResolveLanguage(string? configuredLanguage)
        {
            if (TryParseConfiguredLanguage(configuredLanguage, out var configured))
            {
                return configured;
            }

            return ResolveFromSystemCulture(CultureInfo.CurrentUICulture);
        }

        private static bool TryParseConfiguredLanguage(string? configuredLanguage, out UiLanguage language)
        {
            language = UiLanguage.English;
            if (string.IsNullOrWhiteSpace(configuredLanguage))
            {
                return false;
            }

            var normalized = configuredLanguage.Trim();
            if (string.Equals(normalized, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(normalized, "ja", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "ja-JP", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "japanese", StringComparison.OrdinalIgnoreCase))
            {
                language = UiLanguage.Japanese;
                return true;
            }

            if (string.Equals(normalized, "en", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "en-US", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "en-GB", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "english", StringComparison.OrdinalIgnoreCase))
            {
                language = UiLanguage.English;
                return true;
            }

            return false;
        }

        private static UiLanguage ResolveFromSystemCulture(CultureInfo culture)
        {
            for (var current = culture; current != CultureInfo.InvariantCulture; current = current.Parent)
            {
                if (string.Equals(current.TwoLetterISOLanguageName, "ja", StringComparison.OrdinalIgnoreCase))
                {
                    return UiLanguage.Japanese;
                }
                if (string.Equals(current.TwoLetterISOLanguageName, "en", StringComparison.OrdinalIgnoreCase))
                {
                    return UiLanguage.English;
                }
            }
            return UiLanguage.English;
        }

        // 表示文言プロパティ定義
        public string Label_Port => _language switch
        {
            UiLanguage.English => "COM",
            _ => "COM"
        };

        public string Label_BaudRate => _language switch
        {
            UiLanguage.English => "Baud Rate",
            _ => "速度"
        };

        public string Button_Refresh => _language switch
        {
            UiLanguage.English => "Refresh",
            _ => "更新"
        };

        public string Label_ReliabilityMode => _language switch
        {
            UiLanguage.English => "Mode",
            _ => "転送方式"
        };

        public string Label_DuplexMode => _language switch
        {
            UiLanguage.English => "Duplex",
            _ => "通信"
        };

        public string Label_HalfDuplexControl => _language switch
        {
            UiLanguage.English => "Half-Duplex Ctrl",
            _ => "半二重制御"
        };

        public string Button_Connect => _language switch
        {
            UiLanguage.English => "Connect",
            _ => "接続"
        };

        public string Button_Disconnect => _language switch
        {
            UiLanguage.English => "Disconnect",
            _ => "切断"
        };

        public string Button_Cancel => _language switch
        {
            UiLanguage.English => "Cancel",
            _ => "キャンセル"
        };

        public string Button_SelectFile => _language switch
        {
            UiLanguage.English => "Select Files...",
            _ => "送信ファイルを選択"
        };

        public string Button_OpenDownloads => _language switch
        {
            UiLanguage.English => "Open Downloads",
            _ => "受信したフォルダを開く"
        };

        public string Label_DropFiles => _language switch
        {
            UiLanguage.English => "Drop files or folders here",
            _ => "ファイルまたはフォルダをドロップ"
        };

        public string Label_Description => _language switch
        {
            UiLanguage.English => "Send to connected peer, keeping relative paths and modified time",
            _ => "接続済みの相手へ、相対パスと更新日時を保持して送信します"
        };

        public string Label_Status => _language switch
        {
            UiLanguage.English => "Status",
            _ => "状態"
        };

        public string Label_Target => _language switch
        {
            UiLanguage.English => "Target",
            _ => "対象"
        };

        public string Label_Files => _language switch
        {
            UiLanguage.English => "Files",
            _ => "ファイル"
        };

        public string Label_Folders => _language switch
        {
            UiLanguage.English => "Folders",
            _ => "フォルダ"
        };

        public string Label_Speed => _language switch
        {
            UiLanguage.English => "Speed",
            _ => "速度"
        };

        public string Label_Transferred => _language switch
        {
            UiLanguage.English => "Transferred",
            _ => "転送済み"
        };

        public string Label_Errors => _language switch
        {
            UiLanguage.English => "Errors",
            _ => "誤り数"
        };

        public string Label_Retries => _language switch
        {
            UiLanguage.English => "Retries",
            _ => "再送回数"
        };

        public string Label_CurrentFile => _language switch
        {
            UiLanguage.English => "Current File",
            _ => "現在処理中"
        };

        public string Label_Overall => _language switch
        {
            UiLanguage.English => "Overall",
            _ => "全体"
        };

        public string Label_CurrentFileProgress => _language switch
        {
            UiLanguage.English => "Current File",
            _ => "現在ファイル"
        };

        public string Message_ConnectFirst => _language switch
        {
            UiLanguage.English => "Please connect to COM port first.",
            _ => "先にCOMポートへ接続してください。"
        };

        public string Message_Transferring => _language switch
        {
            UiLanguage.English => "Transfer is in progress.",
            _ => "転送中です。"
        };

        public string Title_SendError => _language switch
        {
            UiLanguage.English => "Send Error",
            _ => "送信エラー"
        };

        public string Message_SelectComPort => _language switch
        {
            UiLanguage.English => "Please select a COM port.",
            _ => "COMポートを選択してください。"
        };

        public string Title_ConnectError => _language switch
        {
            UiLanguage.English => "Connection Error",
            _ => "接続エラー"
        };

        public string Title_CannotOpenFolder => _language switch
        {
            UiLanguage.English => "Cannot open downloads folder",
            _ => "保存場所を開けません"
        };

        public string Title_ConfirmRetry => _language switch
        {
            UiLanguage.English => "Confirm Retry",
            _ => "再送確認"
        };

        public string Title_SelectFiles => _language switch
        {
            UiLanguage.English => "Select files to send",
            _ => "送信するファイルを選択"
        };

        public string Option_ReliabilityArq => _language switch
        {
            UiLanguage.English => "With ARQ",
            _ => "ARQあり"
        };

        public string Option_ReliabilityOneWay => _language switch
        {
            UiLanguage.English => "Without ARQ (One-way)",
            _ => "ARQなし片方向"
        };

        public string Option_DuplexFull => _language switch
        {
            UiLanguage.English => "Full Duplex",
            _ => "全二重"
        };

        public string Option_DuplexHalf => _language switch
        {
            UiLanguage.English => "Half Duplex",
            _ => "半二重"
        };

        public string Option_HalfDuplexDriver => _language switch
        {
            UiLanguage.English => "Driver Managed",
            _ => "ドライバ任せ"
        };

        public string Option_HalfDuplexRts => _language switch
        {
            UiLanguage.English => "RTS Control",
            _ => "RTS制御"
        };
    }
}
