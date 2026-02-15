using Vintagestory.API.MathTools;
using Xunit;

namespace QuernMessage.Tests
{
    public class MessageDeduplicatorTests
    {
        private readonly MessageDeduplicator _deduplicator = new();

        [Fact]
        public void ShouldSend_FirstMessageForPosition_ReturnsTrue()
        {
            var pos = new BlockPos(1, 2, 3);

            var result = _deduplicator.ShouldSend(pos, "Flint", 1000);

            Assert.True(result);
        }

        [Fact]
        public void ShouldSend_SameItemSamePositionWithinWindow_ReturnsFalse()
        {
            var pos = new BlockPos(1, 2, 3);
            _deduplicator.ShouldSend(pos, "Flint", 1000);

            var result = _deduplicator.ShouldSend(pos, "Flint", 1200);

            Assert.False(result);
        }

        [Fact]
        public void ShouldSend_SameItemSamePositionAfterWindow_ReturnsTrue()
        {
            var pos = new BlockPos(1, 2, 3);
            _deduplicator.ShouldSend(pos, "Flint", 1000);

            var result = _deduplicator.ShouldSend(pos, "Flint", 1600);

            Assert.True(result);
        }

        [Fact]
        public void ShouldSend_DifferentItemSamePositionWithinWindow_ReturnsTrue()
        {
            var pos = new BlockPos(1, 2, 3);
            _deduplicator.ShouldSend(pos, "Flint", 1000);

            var result = _deduplicator.ShouldSend(pos, "Bone", 1200);

            Assert.True(result);
        }

        [Fact]
        public void ShouldSend_SameItemDifferentPosition_ReturnsTrue()
        {
            var pos1 = new BlockPos(1, 2, 3);
            var pos2 = new BlockPos(4, 5, 6);
            _deduplicator.ShouldSend(pos1, "Flint", 1000);

            var result = _deduplicator.ShouldSend(pos2, "Flint", 1000);

            Assert.True(result);
        }

        [Fact]
        public void ShouldSend_CustomWindow_RespectsConfiguredDuration()
        {
            var deduplicator = new MessageDeduplicator(deduplicationWindowMs: 1000);
            var pos = new BlockPos(1, 2, 3);
            deduplicator.ShouldSend(pos, "Flint", 1000);

            Assert.False(deduplicator.ShouldSend(pos, "Flint", 1500));
            Assert.False(deduplicator.ShouldSend(pos, "Flint", 1999));
            Assert.True(deduplicator.ShouldSend(pos, "Flint", 2000));
        }

        [Fact]
        public void Clear_ResetsAllState()
        {
            var pos = new BlockPos(1, 2, 3);
            _deduplicator.ShouldSend(pos, "Flint", 1000);

            _deduplicator.Clear();

            Assert.True(_deduplicator.ShouldSend(pos, "Flint", 1000));
        }
    }
}
