using System.Collections.Generic;
using System.IO;
using FTHexMerge.Models;

namespace FTHexMerge.Core
{
    /// <summary>
    /// 逐字节回读校验器：把输出文件再解析一遍，与仲裁结果逐字节比对。
    /// 这是稳定性硬底线（需求 3.4.1 验收）——零差异才放行。
    /// </summary>
    public static class RoundTripVerifier
    {
        /// <summary>
        /// 把写出的 HEX 文本解析后，与期望字节映射逐字节比对。
        /// </summary>
        /// <param name="hexText">HexWriter.Write 的输出。</param>
        /// <param name="expected">仲裁结果（期望的 地址->字节）。</param>
        /// <param name="mismatchAddress">第一个不匹配的地址（失败时填充）。</param>
        /// <returns>true = 完全一致。</returns>
        public static bool Verify(string hexText, SortedDictionary<uint, byte> expected, out uint mismatchAddress)
        {
            // 写到临时文件再用 HexParser 解析（保证走同一条解析路径）
            string tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, hexText);
            try
            {
                MemoryImage parsed = HexParser.Parse(tmp, 0);

                // 逐字节比对：期望的每个地址都要存在且值一致
                foreach (var kv in expected)
                {
                    byte v;
                    if (!parsed.TryGet(kv.Key, out v) || v != kv.Value)
                    {
                        mismatchAddress = kv.Key;
                        return false;
                    }
                }

                // 反向：解析出的每个地址也都要在期望里（避免多输出）
                foreach (uint addr in parsed.Addresses)
                {
                    if (!expected.ContainsKey(addr))
                    {
                        mismatchAddress = addr;
                        return false;
                    }
                }

                mismatchAddress = 0;
                return true;
            }
            finally
            {
                File.Delete(tmp);
            }
        }
    }
}
