using System.Collections.Generic;
using HexMerge.Models;

namespace HexMerge.Core
{
    /// <summary>
    /// 字节级仲裁器（WinMerge 式逐块选择 + 文件优先级默认填充）。
    /// 对每个地址单元决定保留哪个文件的字节：
    /// - 独占：直接保留该唯一来源
    /// - 相同：任取一个（值一致）
    /// - 冲突：按"选择表"，无则按"文件优先级"
    /// 对应需求 3.3.2 / 4.4（字节级覆盖，修复部分重叠漏洞）。
    /// </summary>
    public static class Arbiter
    {
        /// <summary>
        /// 根据比对单元、用户逐块选择、文件优先级，生成最终字节映射。
        /// </summary>
        /// <param name="units">比对单元（按地址升序）。</param>
        /// <param name="choices">用户对某地址的手动选择：地址 -> 选定的文件索引。可为空。</param>
        /// <param name="priority">文件优先级，靠前的优先（如 {0, 1, 2} 表示 BOOT>APP>Pflash）。</param>
        /// <returns>合并后的字节映射（地址 -> 字节），仅含被选中覆盖的字节。</returns>
        public static SortedDictionary<uint, byte> Resolve(
            List<AddressUnit> units,
            Dictionary<uint, int> choices,
            int[] priority)
        {
            SortedDictionary<uint, byte> result = new SortedDictionary<uint, byte>();

            foreach (AddressUnit unit in units)
            {
                int chosenFile = ChooseFile(unit, choices, priority);
                if (chosenFile >= 0)
                {
                    result[unit.Address] = unit.Values[chosenFile];
                }
                // chosenFile < 0 表示该地址无任何来源（不应发生在 units 里），跳过
            }

            return result;
        }

        /// <summary>对单个地址单元决定保留哪个文件。返回文件索引，无来源返回 -1。</summary>
        private static int ChooseFile(AddressUnit unit, Dictionary<uint, int> choices, int[] priority)
        {
            if (unit.Values.Count == 0) return -1;

            // 1) 用户手动选择优先
            int manual;
            if (choices != null && choices.TryGetValue(unit.Address, out manual) && unit.Values.ContainsKey(manual))
            {
                return manual;
            }

            // 2) 独占：唯一来源
            if (unit.Values.Count == 1)
            {
                foreach (var kv in unit.Values) return kv.Key;
            }

            // 3) 相同：任取一个（按优先级取最高的）
            // 4) 冲突：按优先级
            foreach (int fileIndex in priority)
            {
                if (unit.Values.ContainsKey(fileIndex))
                {
                    return fileIndex;
                }
            }

            // 优先级没覆盖到该地址的任何来源：取第一个
            foreach (var kv in unit.Values) return kv.Key;
            return -1;
        }
    }
}
