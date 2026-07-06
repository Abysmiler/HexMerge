using System.Collections.Generic;
using HexMerge.Core;
using HexMerge.Models;
using NUnit.Framework;

namespace HexMerge.Tests
{
    [TestFixture]
    public class ArbiterTests
    {
        [Test]
        public void Resolve_ConflictFollowsPriority()
        {
            MemoryImage a = new MemoryImage("a", 0); a.Set(0, 0xAA);
            MemoryImage b = new MemoryImage("b", 1); b.Set(0, 0x55);

            var units = ConflictDetector.Compare(a, b);
            var result = Arbiter.Resolve(units, null, new int[] { 0, 1 }); // BOOT>APP

            Assert.That(result[0], Is.EqualTo(0xAA));
        }

        [Test]
        public void Resolve_ConflictFollowsManualChoice()
        {
            MemoryImage a = new MemoryImage("a", 0); a.Set(0, 0xAA);
            MemoryImage b = new MemoryImage("b", 1); b.Set(0, 0x55);

            var units = ConflictDetector.Compare(a, b);
            var choices = new Dictionary<uint, int> { { 0, 1 } }; // 手动选 APP
            var result = Arbiter.Resolve(units, choices, new int[] { 0, 1 });

            Assert.That(result[0], Is.EqualTo(0x55));
        }

        [Test]
        public void Resolve_ExclusiveBytesAllKept()
        {
            MemoryImage a = new MemoryImage("a", 0); a.Set(0, 0xAA); a.Set(1, 0xBB);
            MemoryImage b = new MemoryImage("b", 1); b.Set(5, 0xCC);

            var units = ConflictDetector.Compare(a, b);
            var result = Arbiter.Resolve(units, null, new int[] { 0, 1 });

            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result[0], Is.EqualTo(0xAA));
            Assert.That(result[1], Is.EqualTo(0xBB));
            Assert.That(result[5], Is.EqualTo(0xCC));
        }

        [Test]
        public void Resolve_SameValue_StatusIsSameAndKeepsValue()
        {
            MemoryImage a = new MemoryImage("a", 0); a.Set(0, 0x11);
            MemoryImage b = new MemoryImage("b", 1); b.Set(0, 0x11);

            var units = ConflictDetector.Compare(a, b);
            var result = Arbiter.Resolve(units, null, new int[] { 1, 0 });

            Assert.That(units[0].Status, Is.EqualTo(UnitStatus.Same));
            Assert.That(result[0], Is.EqualTo(0x11));
        }

        [Test]
        public void Resolve_ManualChoiceOnNonConflictAddress_StillHonored()
        {
            // 手动选择也应作用于独占/相同地址（用户逐块改选）
            MemoryImage a = new MemoryImage("a", 0); a.Set(0, 0xAA); a.Set(1, 0xAA);
            MemoryImage b = new MemoryImage("b", 1); b.Set(1, 0xBB); // 地址1 冲突

            var units = ConflictDetector.Compare(a, b);
            var choices = new Dictionary<uint, int> { { 1, 1 } }; // 地址1 选 APP
            var result = Arbiter.Resolve(units, choices, new int[] { 0, 1 });

            Assert.That(result[0], Is.EqualTo(0xAA)); // 独占，保留 a
            Assert.That(result[1], Is.EqualTo(0xBB)); // 冲突，手动选 b
        }
    }
}
