using System.Collections.Generic;

namespace FTHexMerge.Models
{
    /// <summary>
    /// 单个文件的字节级地址映射：地址 -> 字节值。
    /// 只保存该文件实际包含数据的地址，便于按地址查找与遍历。
    /// </summary>
    public sealed class MemoryImage
    {
        private readonly SortedDictionary<uint, byte> _bytes;

        public MemoryImage(string fileName, int fileIndex)
        {
            FileName = fileName;
            FileIndex = fileIndex;
            _bytes = new SortedDictionary<uint, byte>();
        }

        /// <summary>文件名（仅文件名，不含路径）。</summary>
        public string FileName { get; private set; }

        /// <summary>文件索引（0/1/2，标识来源文件）。</summary>
        public int FileIndex { get; private set; }

        /// <summary>设置某地址的字节（同地址多次写以最后一次为准）。</summary>
        public void Set(uint address, byte value)
        {
            _bytes[address] = value;
        }

        /// <summary>尝试获取某地址的字节，返回是否存在。</summary>
        public bool TryGet(uint address, out byte value)
        {
            return _bytes.TryGetValue(address, out value);
        }

        /// <summary>按地址升序返回所有地址。</summary>
        public IEnumerable<uint> Addresses
        {
            get { return _bytes.Keys; }
        }

        /// <summary>该文件包含的字节总数。</summary>
        public int Count
        {
            get { return _bytes.Count; }
        }

        /// <summary>是否为空。</summary>
        public bool IsEmpty
        {
            get { return _bytes.Count == 0; }
        }

        /// <summary>最小地址（空时返回 0）。</summary>
        public uint MinAddress
        {
            get
            {
                foreach (uint a in _bytes.Keys) return a;
                return 0;
            }
        }

        /// <summary>最大地址（空时返回 0）。</summary>
        public uint MaxAddress
        {
            get
            {
                uint last = 0;
                foreach (uint a in _bytes.Keys) last = a;
                return last;
            }
        }

        /// <summary>起始线性地址（类型05 记录的程序入口）；null 表示该文件无 05 记录。
        /// 仅"记录"，不作为数据字节参与合并。</summary>
        public uint? StartLinearAddress { get; private set; }

        /// <summary>记录类型05 起始线性地址（程序入口）。注意：不调用 Set，不产生任何数据字节。</summary>
        public void SetStartLinearAddress(uint address) { StartLinearAddress = address; }

        /// <summary>
        /// 将连续地址归并为段列表（当前地址 = 上一末地址 + 1 即视为连续，合并）。
        /// 对应需求 3.1.3 段归并。
        /// </summary>
        public List<Segment> GetSegments()
        {
            List<Segment> segs = new List<Segment>();
            uint? start = null;
            uint? prev = null;
            foreach (uint addr in _bytes.Keys)
            {
                if (start == null)
                {
                    start = addr;
                    prev = addr;
                }
                else if (addr == prev.Value + 1u)
                {
                    prev = addr;
                }
                else
                {
                    segs.Add(new Segment(start.Value, prev.Value, FileIndex, FileName));
                    start = addr;
                    prev = addr;
                }
            }
            if (start != null)
            {
                segs.Add(new Segment(start.Value, prev.Value, FileIndex, FileName));
            }
            return segs;
        }
    }
}
