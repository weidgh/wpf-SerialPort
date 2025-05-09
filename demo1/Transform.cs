using System;
using System.Globalization;
using System.Text;

namespace demo1
{
    public static class Transform
    {
        // 预定义十六进制字符表
        private static readonly char[] HexChars = "0123456789ABCDEF".ToCharArray();

        /// <summary>
        /// 字节数组转十六进制字符串（核心方法）
        /// </summary>
        public static string HexToString(byte[] buffer, string split = "")
        {
            // 基础校验
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (buffer.Length == 0) return "";

            // 计算预期长度
            int splitLen = string.IsNullOrEmpty(split) ? 0 : split.Length;
            var sb = new StringBuilder(buffer.Length * (2 + splitLen));

            for (int i = 0; i < buffer.Length; i++)
            {
                // 转换字节
                byte b = buffer[i];
                sb.Append(HexChars[b >> 4]);   // 高4位
                sb.Append(HexChars[b & 0x0F]); // 低4位

                // 添加分隔符（最后一个不添加）
                if (i < buffer.Length - 1 && splitLen > 0)
                {
                    sb.Append(split);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 带起始索引的扩展方法
        /// </summary>
        public static string HexToString(byte[] buffer, int startIndex, string split = "")
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (startIndex < 0 || startIndex >= buffer.Length)
                throw new IndexOutOfRangeException("起始位置越界");

            // 计算实际可用长度
            int validLength = buffer.Length - startIndex;
            return HexToStringImpl(buffer, startIndex, validLength, split);
        }

        // 实际转换实现
        private static string HexToStringImpl(byte[] buffer, int start, int length, string split)
        {
            if (length == 0) return "";

            int splitLen = split.Length;
            var sb = new StringBuilder(length * (2 + splitLen));
            int end = start + length;

            for (int i = start; i < end; i++)
            {
                byte b = buffer[i];
                sb.Append(HexChars[b >> 4]);
                sb.Append(HexChars[b & 0x0F]);

                if (i < end - 1 && splitLen > 0)
                {
                    sb.Append(split);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 十六进制字符串转字节数组
        /// </summary>
        public static byte[] HexToBytes(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Array.Empty<byte>();

            hex = hex.Trim().Replace(" ", "");
            if (hex.Length % 2 != 0)
                throw new ArgumentException("HEX字符串长度必须为偶数");

            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                string hexByte = hex.Substring(i * 2, 2);
                if (!byte.TryParse(hexByte, NumberStyles.HexNumber, null, out bytes[i]))
                    throw new FormatException($"无效的HEX字符: {hexByte}");
            }
            return bytes;
        }
    }
}