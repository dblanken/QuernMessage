using System.Reflection;
using Moq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;

namespace QuernMessage.Tests
{
    public class QuernMessageHandlerTests : IDisposable
    {
        private readonly Mock<IMessageSender> _mockSender;
        private readonly MessageDeduplicator _deduplicator;

        public QuernMessageHandlerTests()
        {
            _mockSender = new Mock<IMessageSender>();
            _deduplicator = new MessageDeduplicator();
            QuernMessageHandler.Configure(_deduplicator, _mockSender.Object);
        }

        public void Dispose()
        {
            QuernMessageHandler.ResetToDefaults();
        }

        [Fact]
        public void SendInvalidItemMessage_NonServerApi_DoesNotSend()
        {
            var mockApi = new Mock<ICoreAPI>();
            var blockEntity = CreateBlockEntityWithApi(mockApi.Object);
            var stack = CreateItemStack("Flint");

            QuernMessageHandler.SendInvalidItemMessage(blockEntity, stack);

            _mockSender.Verify(
                s => s.SendToNearbyPlayers(It.IsAny<BlockPos>(), It.IsAny<string>()),
                Times.Never);
        }

        [Fact]
        public void SendInvalidItemMessage_ServerApi_SendsMessage()
        {
            var sapi = CreateServerApi(elapsedMs: 1000);
            var blockEntity = CreateBlockEntityWithApi(sapi);
            var stack = CreateItemStack("Flint");

            QuernMessageHandler.SendInvalidItemMessage(blockEntity, stack);

            _mockSender.Verify(
                s => s.SendToNearbyPlayers(
                    It.IsAny<BlockPos>(),
                    It.Is<string>(msg => msg.Contains("Flint") && msg.Contains("cannot be ground"))),
                Times.Once);
        }

        [Fact]
        public void SendInvalidItemMessage_DuplicateWithinWindow_SkipsSecondMessage()
        {
            var sapi = CreateServerApi(elapsedMs: 1000);
            var blockEntity = CreateBlockEntityWithApi(sapi);
            var stack = CreateItemStack("Flint");

            QuernMessageHandler.SendInvalidItemMessage(blockEntity, stack);
            QuernMessageHandler.SendInvalidItemMessage(blockEntity, stack);

            _mockSender.Verify(
                s => s.SendToNearbyPlayers(It.IsAny<BlockPos>(), It.IsAny<string>()),
                Times.Once);
        }

        private static ICoreServerAPI CreateServerApi(long elapsedMs)
        {
            var mockWorld = new Mock<IServerWorldAccessor>();
            mockWorld.Setup(w => w.ElapsedMilliseconds).Returns(elapsedMs);

            var mockSapi = new Mock<ICoreServerAPI>();
            mockSapi.Setup(a => a.World).Returns(mockWorld.Object);

            return mockSapi.Object;
        }

        private static BlockEntity CreateBlockEntityWithApi(ICoreAPI api)
        {
            var blockEntity = new TestBlockEntity();
            blockEntity.SetApi(api);
            blockEntity.SetPos(new BlockPos(1, 2, 3));
            return blockEntity;
        }

        private static ItemStack CreateItemStack(string name)
        {
            var mockItem = new Mock<Item>();
            mockItem.Setup(c => c.GetHeldItemName(It.IsAny<ItemStack>())).Returns(name);

            var stack = new ItemStack();
            var itemField = typeof(ItemStack).GetField("item", BindingFlags.Instance | BindingFlags.NonPublic);
            itemField!.SetValue(stack, mockItem.Object);
            stack.Class = EnumItemClass.Item;
            return stack;
        }
    }

    public class TestBlockEntity : BlockEntity
    {
        public void SetApi(ICoreAPI api)
        {
            Api = api;
        }

        public void SetPos(BlockPos pos)
        {
            Pos = pos;
        }
    }
}
