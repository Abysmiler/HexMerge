using System;
using System.IO;
using HexMerge.Models;

namespace HexMerge.Core
{
    /// <summary>
    /// 固件文件加载分发：按扩展名选 HEX 或 DAT 解析器。
    /// 集中 DAT 判定，供 ViewModel 复用（避免各处重复 if .dat）。
    /// </summary>
    public static class FirmwareLoader
    {
        /// <summary>是否为 DAT 文件（DFlash 二进制数据，需基地址）。</summary>
        public static bool IsDat(string path)
        {
            return !string.IsNullOrEmpty(path)
                && Path.GetExtension(path).Equals(".dat", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>按扩展名加载：.dat 用基地址走 BinaryDatParser，其余按 HEX 解析。</summary>
        public static MemoryImage Load(string path, uint baseAddr, int fileIndex)
        {
            if (IsDat(path)) return BinaryDatParser.Parse(path, baseAddr, fileIndex);
            return HexParser.Parse(path, fileIndex);
        }
    }
}
