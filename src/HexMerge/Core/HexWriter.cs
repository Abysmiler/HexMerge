using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace FTHexMerge.Core
{
    /// <summary>
    /// 合并结果写出为标准 Intel HEX（I32HEX，类型04 扩展线性地址）。
    /// 对应需求 3.4.1：按地址升序，每条数据记录默认 16 字节，末尾追加 EOF。
    /// </summary>
    public static class HexWriter
    {
        /// <summary>每条数据记录的字节数。</summary>
        public const int BytesPerLine = 16;

        /// <summary>
        /// 把合并后的字节映射写成 I32HEX 文本。
        /// </summary>
        /// <param name="bytes">地址 -> 字节（按地址升序遍历）。</param>
        public static string Write(SortedDictionary<uint, byte> bytes)
        {
            return Write(bytes, null);
        }

        /// <summary>写出 I32HEX 文本；startAddr 非空时在 EOF 前追加类型05 起始线性地址记录（程序入口）。</summary>
        public static string Write(SortedDictionary<uint, byte> bytes, uint? startAddr)
        {
            StringBuilder sb = new StringBuilder();
            uint upper = 0xFFFFFFFF; // 尚未输出过类型04；用非法值强制首次输出
            uint pendingAddr = 0;
            int pendingLen = 0;
            byte[] line = new byte[BytesPerLine];
            uint expectedNext = 0; // 当前缓冲行期望的下一个连续地址

            foreach (var kv in bytes)
            {
                uint addr = kv.Key;
                byte val = kv.Value;

                // 满 16 字节，或地址不连续（跳变）-> 刷新当前行（HEX 数据记录本就要求连续）
                bool needFlush = pendingLen == BytesPerLine
                                 || (pendingLen > 0 && addr != expectedNext);

                if (needFlush)
                {
                    FlushLine(sb, ref upper, pendingAddr, line, pendingLen);
                    pendingLen = 0;
                }

                if (pendingLen == 0)
                {
                    pendingAddr = addr;
                }

                line[pendingLen] = val;
                expectedNext = addr + 1;
                pendingLen++;
            }

            if (pendingLen > 0)
            {
                FlushLine(sb, ref upper, pendingAddr, line, pendingLen);
            }

            // 起始线性地址（类型05，程序入口）：EOF 前输出（若有）。不依赖 upper，地址字段固定 0。
            if (startAddr.HasValue)
            {
                uint s = startAddr.Value;
                sb.Append(':');
                sb.Append("04");      // 数据长度 4
                sb.Append("0000");    // 地址字段固定 0
                sb.Append("05");      // 类型 05
                AppendByte(sb, (byte)(s >> 24));
                AppendByte(sb, (byte)(s >> 16));
                AppendByte(sb, (byte)(s >> 8));
                AppendByte(sb, (byte)(s & 0xFF));
                AppendChecksum(sb, 0x04, 0x00, 0x00, 0x05,
                               (byte)(s >> 24), (byte)(s >> 16), (byte)(s >> 8), (byte)(s & 0xFF));
                sb.AppendLine();
            }

            sb.AppendLine(":00000001FF"); // EOF
            return sb.ToString();
        }

        /// <summary>写一条数据记录（必要时先写类型04 扩展线性地址）。</summary>
        private static void FlushLine(StringBuilder sb, ref uint upper, uint addr, byte[] line, int len)
        {
            uint curUpper = addr >> 16;
            if (curUpper != upper)
            {
                // 类型 04：扩展线性地址
                sb.Append(':');
                sb.Append("02");
                sb.Append("0000");
                sb.Append("04");
                AppendByte(sb, (byte)(curUpper >> 8));
                AppendByte(sb, (byte)(curUpper & 0xFF));
                AppendChecksum(sb, 0x02, 0x00, 0x00, 0x04,
                               (byte)(curUpper >> 8), (byte)(curUpper & 0xFF));
                sb.AppendLine();
                upper = curUpper;
            }

            // 类型 00：数据记录
            sb.Append(':');
            AppendByte(sb, (byte)len);
            AppendByte(sb, (byte)(addr >> 8));
            AppendByte(sb, (byte)(addr & 0xFF));
            sb.Append("00");
            int sum = len + (int)((addr >> 8) & 0xFF) + (int)(addr & 0xFF) + 0x00;
            for (int i = 0; i < len; i++)
            {
                AppendByte(sb, line[i]);
                sum += line[i];
            }
            AppendChecksumRaw(sb, sum);
            sb.AppendLine();
        }

        private static void AppendByte(StringBuilder sb, byte b)
        {
            sb.Append(b.ToString("X2"));
        }

        private static void AppendChecksum(StringBuilder sb, params int[] bytes)
        {
            int sum = 0;
            for (int i = 0; i < bytes.Length; i++) sum += bytes[i];
            AppendChecksumRaw(sb, sum);
        }

        private static void AppendChecksumRaw(StringBuilder sb, int sum)
        {
            int cc = (~sum + 1) & 0xFF;
            sb.Append(cc.ToString("X2"));
        }
    }
}
