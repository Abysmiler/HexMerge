using System.Collections.Generic;
using HexMerge.Models;

namespace HexMerge.Core
{
    /// <summary>
    /// 字节级冲突检测器。把多个文件的 MemoryImage 合并到统一地址空间，
    /// 逐地址判定 相同 / 冲突 / 独占。
    /// 对应需求 4.2（完全/部分/包含重叠，字节级统一处理）。
    /// </summary>
    public static class ConflictDetector
    {
        /// <summary>
        /// 比对多个文件，返回按地址升序排列的地址单元列表。
        /// </summary>
        /// <param name="images">各文件的字节映射（至少一个）。</param>
        public static List<AddressUnit> Compare(params MemoryImage[] images)
        {
            // 收集所有出现过的地址（去重）
            SortedSet<uint> allAddrs = new SortedSet<uint>();
            for (int i = 0; i < images.Length; i++)
            {
                foreach (uint a in images[i].Addresses)
                {
                    allAddrs.Add(a);
                }
            }

            List<AddressUnit> result = new List<AddressUnit>(allAddrs.Count);
            foreach (uint addr in allAddrs)
            {
                AddressUnit unit = new AddressUnit(addr);

                // 收集该地址在各文件的字节
                for (int i = 0; i < images.Length; i++)
                {
                    byte v;
                    if (images[i].TryGet(addr, out v))
                    {
                        unit.Values[i] = v;
                    }
                }

                // 判定状态
                if (unit.Values.Count <= 1)
                {
                    unit.Status = UnitStatus.Exclusive;
                }
                else
                {
                    // 多个文件有此地址：值是否全相同
                    bool same = true;
                    int firstFile = -1;
                    byte firstVal = 0;
                    foreach (var kv in unit.Values)
                    {
                        if (firstFile < 0) { firstFile = kv.Key; firstVal = kv.Value; }
                        else if (kv.Value != firstVal) { same = false; break; }
                    }
                    unit.Status = same ? UnitStatus.Same : UnitStatus.Conflict;
                }

                result.Add(unit);
            }

            return result;
        }

        /// <summary>统计冲突地址数。</summary>
        public static int CountConflicts(List<AddressUnit> units)
        {
            int n = 0;
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i].Status == UnitStatus.Conflict) n++;
            }
            return n;
        }
    }
}
