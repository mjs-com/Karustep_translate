using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VoiceRecorder
{
    /// <summary>
    /// セッションのインデックス（①、②、③...）を取得するConverter
    /// </summary>
    public class SessionIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is RecordingSession session)
            {
                // MainWindowのSessionsコレクションからインデックスを取得
                var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                if (mainWindow != null && mainWindow.Sessions != null)
                {
                    int index = mainWindow.Sessions.IndexOf(session);
                    if (index >= 0)
                    {
                        // ①、②、③...の形式で返す
                        return GetCircleNumber(index + 1);
                    }
                }
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private string GetCircleNumber(int number)
        {
            // ①～⑳まで対応
            if (number >= 1 && number <= 20)
            {
                return ((char)(0x2460 + number - 1)).ToString();
            }
            // 21以上は数字で表示
            return number.ToString();
        }
    }

    /// <summary>
    /// 録音開始時刻を表示用文字列に変換するConverter
    /// </summary>
    public class TimeDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dateTime)
            {
                return dateTime.ToString("HH:mm");
            }
            // 録音開始前は "00:00" を表示
            return "00:00";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// セッションの状態に応じたアイコンを返すConverter (MultiBinding対応)
    /// </summary>
    public class SessionStatusIconConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 3 &&
                values[0] is bool isRecording &&
                values[1] is bool isPaused &&
                values[2] is bool isStopped)
            {
                // 録音中（一時停止していない）→ 赤丸
                if (isRecording && !isPaused)
                {
                    return "●";
                }
                // 一時停止中 → オレンジの一時停止マーク
                else if (isPaused)
                {
                    return "⏸";
                }
                // 停止済み（要約処理完了）→ 緑のチェックボックス
                else if (isStopped)
                {
                    return "☑";
                }
            }
            // デフォルトは何も表示しない
            return "";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// セッションの状態に応じた色を返すConverter (MultiBinding対応)
    /// </summary>
    public class SessionStatusColorConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 3 &&
                values[0] is bool isRecording &&
                values[1] is bool isPaused &&
                values[2] is bool isStopped)
            {
                // 録音中（一時停止していない）→ 赤
                if (isRecording && !isPaused)
                {
                    return new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x00));
                }
                // 一時停止中 → オレンジ
                else if (isPaused)
                {
                    return new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00));
                }
                // 停止済み（要約処理完了）→ 緑
                else if (isStopped)
                {
                    return new SolidColorBrush(Color.FromRgb(0x00, 0xC8, 0x53));
                }
            }
            // デフォルトは透明
            return new SolidColorBrush(Colors.Transparent);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

