using System.Reflection;
using Moq;
using Vintagestory.API.Common;
using Xunit;

namespace QuernMessage.Tests
{
    public class QuernValidationTests
    {
        [Fact]
        public void CanBeGround_NullApi_ReturnsFalse()
        {
            var stack = new ItemStack();

            Assert.False(QuernValidation.CanBeGround(null, stack));
        }

        [Fact]
        public void CanBeGround_NullStack_ReturnsFalse()
        {
            var api = new Mock<ICoreAPI>();

            Assert.False(QuernValidation.CanBeGround(api.Object, null));
        }

        [Fact]
        public void CanBeGround_NullCollectible_ReturnsFalse()
        {
            var api = CreateMockApi();
            var stack = new ItemStack();

            Assert.False(QuernValidation.CanBeGround(api, stack));
        }

        [Fact]
        public void CanBeGround_CollectibleWithNoGrindingProps_ReturnsFalse()
        {
            var api = CreateMockApi();
            var collectible = new CollectibleWithoutGrinding();
            var stack = CreateStackWithCollectible(collectible);

            Assert.False(QuernValidation.CanBeGround(api, stack));
        }

        [Fact]
        public void CanBeGround_CollectibleWithNullGrindingProps_ReturnsFalse()
        {
            var api = CreateMockApi();
            var collectible = new CollectibleWithNullGrinding();
            var stack = CreateStackWithCollectible(collectible);

            Assert.False(QuernValidation.CanBeGround(api, stack));
        }

        [Fact]
        public void CanBeGround_CollectibleWithGrindingPropsButNullGroundStack_ReturnsFalse()
        {
            var api = CreateMockApi();
            var collectible = new CollectibleWithNullGroundStack();
            var stack = CreateStackWithCollectible(collectible);

            Assert.False(QuernValidation.CanBeGround(api, stack));
        }

        [Fact]
        public void CanBeGround_CollectibleWithValidGrindingProps_ReturnsTrue()
        {
            var api = CreateMockApi();
            var collectible = new CollectibleWithValidGrinding();
            var stack = CreateStackWithCollectible(collectible);

            Assert.True(QuernValidation.CanBeGround(api, stack));
        }

        private static ICoreAPI CreateMockApi()
        {
            var mockLogger = new Mock<ILogger>();
            var mockApi = new Mock<ICoreAPI>();
            mockApi.Setup(a => a.Logger).Returns(mockLogger.Object);
            return mockApi.Object;
        }

        private static ItemStack CreateStackWithCollectible(CollectibleObject collectible)
        {
            var stack = new ItemStack();
            // Collectible is read-only; set the underlying Item field via reflection
            var itemField = typeof(ItemStack).GetField("item", BindingFlags.Instance | BindingFlags.NonPublic);
            itemField!.SetValue(stack, collectible);
            stack.Class = EnumItemClass.Item;
            return stack;
        }
    }

    // Test doubles that expose properties for reflection to discover

    public class CollectibleWithoutGrinding : Item
    {
        // No GrindingProps property — base Item may have one, but it returns null by default
    }

    public class CollectibleWithNullGrinding : Item
    {
        public new object? GrindingProps => null;
    }

    public class FakeGrindingPropsNullGroundStack
    {
        public object? GroundStack => null;
    }

    public class CollectibleWithNullGroundStack : Item
    {
        public new FakeGrindingPropsNullGroundStack GrindingProps => new();
    }

    public class FakeGrindingPropsValid
    {
        public object GroundStack => new object();
    }

    public class CollectibleWithValidGrinding : Item
    {
        public new FakeGrindingPropsValid GrindingProps => new();
    }
}
