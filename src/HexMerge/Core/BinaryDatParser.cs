using System.IO;
using HexMerge.Models;

namespace HexMerge.Core
{
    /// <summary>
    /// 二进制 .dat 文件解析器。
    /// 文件是纯原始字节（非 HEX），用户需指定基地址；
    /// 文件第 i 个字节 -> 地址 (baseAddress + i)。
    /// </summary>
    public static class BinaryDatParser
    {
        /// <summary>
        /// 解析二进制文件为字节级地址映射。
        /// </summary>
        /// <param name="path">文件路径。</param>
        /// <param name="baseAddress">基地址（用户手动指定，可由文件名预填）。</param>
        /// <param name="fileIndex">文件索引（标识来源）。</param>
        public static MemoryImage Parse(string path, uint baseAddress, int fileIndex)
        {
            MemoryImage image = new MemoryImage(Path.GetFileName(path), fileIndex);

            // 分块流式读：避免 File.ReadAllBytes 把整个文件一次性载入内存（大 DAT OOM 根因）
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] buf = new byte[65536];
                uint addr = baseAddress;
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    for (int i = 0; i < n; i++)
                        image.Set(addr + (uint)i, buf[i]);
                    addr += (uint)n;
                }
            }

            return image;
        }
    }
}
