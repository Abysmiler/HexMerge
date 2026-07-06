using System.Collections.Generic;

namespace FTHexMerge.Models
{
    /// <summary>
    /// 一个地址单元的比对结果：该地址在各文件中的字节情况。
    /// 字节级模型的核心数据结构（修复需求 4.4 部分重叠漏洞）。
    /// </summary>
    public sealed class AddressUnit
    {
        /// <summary>地址。</summary>
        public uint Address;

        /// <summary>按文件索引存放的字节（不存在则不含该键）。</summary>
        public Dictionary<int, byte> Values;

        /// <summary>比对状态。</summary>
        public UnitStatus Status;

        public AddressUnit(uint address)
        {
            Address = address;
            Values = new Dictionary<int, byte>();
            Status = UnitStatus.Same;
        }
    }

    /// <summary>地址单元的比对状态。</summary>
    public enum UnitStatus
    {
        /// <summary>多个文件在此地址都有数据且值相同（或仅一个文件有数据，无冲突）。</summary>
        Same,

        /// <summary>多个文件在此地址都有数据但值不同 -> 冲突，需仲裁。</summary>
        Conflict,

        /// <summary>仅一个文件有数据 -> 独占（无冲突，直接保留）。</summary>
        Exclusive
    }
}
