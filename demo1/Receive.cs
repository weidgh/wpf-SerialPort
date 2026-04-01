using System;

namespace demo1
{
    /// <summary>
    /// 接收数据帧（含时间戳和显示内容）。
    /// </summary>
    public sealed class ReceiveFrame
    {
        public ReceiveFrame(DateTime timestamp, string displayText)
        {
            Timestamp = timestamp;
            DisplayText = displayText ?? string.Empty;
        }

        public DateTime Timestamp { get; }

        public string DisplayText { get; }
    }
}
