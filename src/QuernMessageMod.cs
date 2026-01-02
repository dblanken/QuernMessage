using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace QuernMessage
{
    public class QuernMessageMod : ModSystem
    {
        private Harmony harmony;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            // Apply Harmony patches
            harmony = new Harmony("com.quernmessage.patches");

            // Find the BlockEntityQuern type dynamically from the survival mod
            var quernType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "BlockEntityQuern");

            if (quernType != null)
            {
                // Patch OnSlotModifid (note: typo in game code)
                var onSlotModifiedMethod = quernType.GetMethod("OnSlotModifid",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (onSlotModifiedMethod != null)
                {
                    var postfixMethod = typeof(QuernSlotPatch).GetMethod("Postfix");
                    harmony.Patch(onSlotModifiedMethod, postfix: new HarmonyMethod(postfixMethod));
                    api.Logger.Notification("[QuernMessage] Successfully patched BlockEntityQuern.OnSlotModifid");
                }
                else
                {
                    api.Logger.Warning("[QuernMessage] Could not find OnSlotModifid method on BlockEntityQuern");
                }

                // Patch OnPlayerRightClick to show error message when GUI opens
                var onPlayerRightClickMethod = quernType.GetMethod("OnPlayerRightClick",
                    BindingFlags.Instance | BindingFlags.Public);

                if (onPlayerRightClickMethod != null)
                {
                    var rightClickPostfix = typeof(QuernRightClickPatch).GetMethod("Postfix");
                    harmony.Patch(onPlayerRightClickMethod, postfix: new HarmonyMethod(rightClickPostfix));
                    api.Logger.Notification("[QuernMessage] Successfully patched BlockEntityQuern.OnPlayerRightClick");
                }
            }
            else
            {
                api.Logger.Warning("[QuernMessage] Could not find BlockEntityQuern type");
            }
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll("com.quernmessage.patches");
            base.Dispose();
        }
    }

    public static class QuernValidation
    {
        public static bool CanBeGround(ICoreAPI api, ItemStack stack)
        {
            if (api == null || stack == null) return false;

            try
            {
                // Try to find a grinding recipe that matches this input
                // Access the recipe registry through reflection since it's not in the public API
                var recipeRegistryType = api.World.GetType()
                    .GetProperty("GrindingRecipes", BindingFlags.Instance | BindingFlags.Public)
                    ?.PropertyType;

                var grindingRecipes = api.World.GetType()
                    .GetProperty("GrindingRecipes", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(api.World);

                if (grindingRecipes == null) return false;

                // Iterate through grinding recipes
                var recipesEnumerable = grindingRecipes as System.Collections.IEnumerable;
                if (recipesEnumerable != null)
                {
                    foreach (var recipe in recipesEnumerable)
                    {
                        // Get the Ingredient property
                        var ingredientProp = recipe.GetType()
                            .GetProperty("Ingredient", BindingFlags.Instance | BindingFlags.Public);

                        if (ingredientProp != null)
                        {
                            var ingredient = ingredientProp.GetValue(recipe);

                            // Check if this ingredient matches the stack
                            var satisfiesMethod = ingredient?.GetType()
                                .GetMethod("SatisfiesAsIngredient", new[] { typeof(ItemStack) });

                            if (satisfiesMethod != null)
                            {
                                bool satisfies = (bool)satisfiesMethod.Invoke(ingredient, new object[] { stack });
                                if (satisfies) return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                api?.Logger?.Debug("[QuernMessage] Error checking grinding recipes: {0}", ex.Message);
            }

            return false;
        }
    }

    // Shared message handler with deduplication
    public static class QuernMessageHandler
    {
        private static readonly System.Collections.Generic.Dictionary<BlockPos, (string itemName, long timestamp)> lastMessages
            = new System.Collections.Generic.Dictionary<BlockPos, (string, long)>();

        public static void SendInvalidItemMessage(BlockEntity blockEntity, ItemStack stack)
        {
            // Only send from server side to prevent duplicates
            if (!(blockEntity.Api is ICoreServerAPI sapi)) return;

            string itemName = stack.GetName();
            long currentTime = sapi.World.ElapsedMilliseconds;

            // Deduplication: Don't send the same message for the same position within 500ms
            if (lastMessages.TryGetValue(blockEntity.Pos, out var lastMsg))
            {
                if (lastMsg.itemName == itemName && (currentTime - lastMsg.timestamp) < 500)
                {
                    return; // Skip duplicate message
                }
            }

            // Update last message timestamp
            lastMessages[blockEntity.Pos] = (itemName, currentTime);

            string errorMessage = $"'{itemName}' cannot be ground in a quern.";

            // Send to all nearby players
            foreach (var player in sapi.World.AllOnlinePlayers)
            {
                if (player.Entity?.Pos != null &&
                    player.Entity.Pos.DistanceTo(blockEntity.Pos.ToVec3d()) < 10)
                {
                    var serverPlayer = player as IServerPlayer;
                    serverPlayer?.SendMessage(
                        GlobalConstants.GeneralChatGroup,
                        errorMessage,
                        EnumChatType.Notification
                    );
                }
            }
        }
    }

    public class QuernSlotPatch
    {
        public static void Postfix(BlockEntity __instance, int slotid)
        {
            try
            {
                // Only check the input slot (slot 0)
                if (slotid != 0) return;

                var inventory = __instance.GetType()
                    .GetProperty("Inventory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(__instance) as InventoryBase;

                if (inventory == null || inventory.Count == 0) return;

                var slot = inventory[0];
                if (slot == null || slot.Empty) return;

                ItemStack inputStack = slot.Itemstack;
                if (inputStack == null) return;

                // Check if this item can be ground
                bool canBeGround = QuernValidation.CanBeGround(__instance.Api, inputStack);

                if (!canBeGround)
                {
                    // Send message using shared handler with deduplication
                    QuernMessageHandler.SendInvalidItemMessage(__instance, inputStack);
                }
            }
            catch (Exception ex)
            {
                __instance.Api?.Logger?.Error("[QuernMessage] Error in QuernSlotPatch: {0}", ex.Message);
            }
        }
    }

    // Patch for when player right-clicks quern - show error if invalid item
    public class QuernRightClickPatch
    {
        public static void Postfix(BlockEntity __instance, IPlayer byPlayer, BlockSelection blockSel, ref bool __result)
        {
            try
            {
                // Only run on server side
                if (!(__instance.Api is ICoreServerAPI sapi)) return;

                // Get the inventory from the quern
                var inventoryProp = __instance.GetType()
                    .GetProperty("Inventory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (inventoryProp == null) return;

                var inventory = inventoryProp.GetValue(__instance) as InventoryBase;
                if (inventory == null || inventory.Count == 0) return;

                // Check slot 0 (input slot)
                var inputSlot = inventory[0];
                if (inputSlot == null || inputSlot.Empty) return;

                ItemStack inputStack = inputSlot.Itemstack;
                if (inputStack == null) return;

                // Check if this item can be ground
                bool canBeGround = QuernValidation.CanBeGround(sapi, inputStack);

                if (canBeGround) return;

                // Item is invalid, send message using shared handler with deduplication
                QuernMessageHandler.SendInvalidItemMessage(__instance, inputStack);
            }
            catch (Exception ex)
            {
                __instance.Api?.Logger?.Error("[QuernMessage] Error in QuernRightClickPatch: {0}", ex.Message);
            }
        }
    }
}
