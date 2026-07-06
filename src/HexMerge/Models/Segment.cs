namespace FTHexMerge.Models
{
    /// <summary>
    /// 一段连续的地址区间 [Start, End]（含两端）。
    /// </summary>
    public sealed class Segment
    {
        /// <summary>起始地址（含）。</summary>
        public uint Start { get; set; }

        /// <summary>结束地址（含）。</summary>
        public uint End { get; set; }

        /// <summary>所属文件索引。</summary>
        public int FileIndex { get; set; }

        /// <summary>所属文件名。</summary>
        public string FileName { get; set; }

        /// <summary>段长度（字节数）。</summary>
        public uint Length
        {
            get { return End - Start + 1; }
        }

        public Segment(uint start, uint end, int fileIndex, string fileName)
        {
            Start = start;
            End = end;
            FileIndex = fileIndex;
            FileName = fileName;
        }
    }
}
