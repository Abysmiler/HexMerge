using System;
using System.Globalization;
using System.IO;
using HexMerge.Models;

namespace HexMerge.Core
{
    /// <summary>
    /// Intel HEX 文件解析器。支持 I8/I16/I32。
    /// 记录类型：00 数据、01 EOF、02 扩展段地址、03 起始段地址(忽略)、
    /// 04 扩展线性地址、05 起始线性地址(忽略)；其它未知类型容错忽略。
    /// </summary>
    public static class HexParser
    {
        /// <summary>
        /// 解析 HEX 文件为字节级地址映射。
        /// </summary>
        /// <param name="path">文件路径。</param>
        /// <param name="fileIndex">文件索引（用于标识来源）。</param>
        /// <exception cref="HexParseException">校验和失败或格式异常时抛出，含行号。</exception>
        public static MemoryImage Parse(string path, int fileIndex)
        {
            MemoryImage image = new MemoryImage(Path.GetFileName(path), fileIndex);
            uint baseAddress = 0; // 由类型 02/04 决定的高位基地址

            // 流式逐行读：避免 File.ReadAllLines 把整个文件一次性载入内存（大文件 OOM 根因）
            using (StreamReader reader = new StreamReader(path))
            {
                int lineNumber = 0;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    line = line.TrimEnd('\r', ' ', '\t'); // ReadLine 已去 \n，再清尾部空白
                    if (line.Length == 0) continue;

                    if (line[0] != ':')
                    {
                        throw new HexParseException(lineNumber, "行不以 ':' 开头");
                    }

                    byte[] record = ParseHexBytes(line.Substring(1), lineNumber);
                    if (record.Length < 5)
                    {
                        throw new HexParseException(lineNumber, "记录长度不足");
                    }

                    int dataLen = record[0];
                    if (record.Length != dataLen + 5)
                    {
                        throw new HexParseException(lineNumber, "数据长度字段与实际不符");
                    }

                    // 校验和：全部字节（含校验和字节）求和的低字节应为 0
                    int sum = 0;
                    for (int j = 0; j < record.Length; j++) sum += record[j];
                    if ((sum & 0xFF) != 0)
                    {
                        throw new HexParseException(lineNumber, "校验和错误");
                    }

                    uint addr = (uint)((record[1] << 8) | record[2]);
                    int recordType = record[3];

                    switch (recordType)
                    {
                        case 0x00: // 数据记录
                            for (int k = 0; k < dataLen; k++)
                            {
                                image.Set(baseAddress + addr + (uint)k, record[4 + k]);
                            }
                            break;
                        case 0x01: // 文件结束
                            return image;
                        case 0x02: // 扩展段地址：左移 4 位
                            baseAddress = ((uint)record[4] << 12) | ((uint)record[5] << 4);
                            break;
                        case 0x03: // 起始段地址：忽略（需求未要求记录）
                            break;
                        case 0x05: // 起始线性地址（程序入口）：记录但不作为数据合并
                            if (dataLen >= 4)
                                image.SetStartLinearAddress(
                                    ((uint)record[4] << 24) | ((uint)record[5] << 16) |
                                    ((uint)record[6] << 8) | record[7]);
                            break;
                        case 0x04: // 扩展线性地址：左移 16 位
                            baseAddress = ((uint)record[4] << 24) | ((uint)record[5] << 16);
                            break;
                        default: // 未知类型：容错忽略
                            break;
                    }
                }
            }

            return image;
        }

        /// <summary>把 hex 字符串（每两个字符一字节）转成字节数组。</summary>
        private static byte[] ParseHexBytes(string hex, int lineNumber)
        {
            if ((hex.Length & 1) != 0)
            {
                throw new HexParseException(lineNumber, "十六进制字符数为奇数");
            }
            byte[] result = new byte[hex.Length / 2];
            for (int i = 0; i < result.Length; i++)
            {
                string s = hex.Substring(i * 2, 2);
                byte b;
                if (!byte.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                {
                    throw new HexParseException(lineNumber, "非法十六进制字符: " + s);
                }
                result[i] = b;
            }
            return result;
        }
    }

    /// <summary>HEX 解析异常，携带出错行号。</summary>
    public class HexParseException : Exception
    {
        /// <summary>出错的行号（从 1 开始）。</summary>
        public int LineNumber { get; private set; }

        public HexParseException(int lineNumber, string message)
            : base("第 " + lineNumber + " 行: " + message)
        {
            LineNumber = lineNumber;
        }
    }
}
