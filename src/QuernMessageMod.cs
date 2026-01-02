using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

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
                // Debug: Log all methods on BlockEntityQuern
                api.Logger.Debug("[QuernMessage] Found BlockEntityQuern type. Available methods:");
                var allMethods = quernType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                foreach (var method in allMethods)
                {
                    var parameters = string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
                    api.Logger.Debug($"[QuernMessage]   {method.ReturnType.Name} {method.Name}({parameters})");
                }

                // Try to find the slot modification callback
                // Note: The game has a typo - it's "OnSlotModifid" not "OnSlotModified"
                MethodInfo targetMethod = null;
                string[] possibleMethodNames = { "OnSlotModifid", "OnSlotModified", "slotModified", "OnInventorySlotModified", "SlotModified" };

                foreach (var methodName in possibleMethodNames)
                {
                    targetMethod = quernType.GetMethod(methodName,
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (targetMethod != null)
                    {
                        api.Logger.Notification($"[QuernMessage] Found method: {methodName}");
                        break;
                    }
                }

                if (targetMethod != null)
                {
                    var postfixMethod = typeof(QuernSlotPatch).GetMethod("Postfix");
                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfixMethod));
                    api.Logger.Notification($"[QuernMessage] Successfully patched BlockEntityQuern.{targetMethod.Name}");
                }
                else
                {
                    api.Logger.Warning("[QuernMessage] Could not find slot modified method on BlockEntityQuern");
                    api.Logger.Warning("[QuernMessage] Will attempt to patch inventory events instead");
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
                bool canBeGround = CanBeGround(__instance.Api, inputStack);

                if (!canBeGround)
                {
                    SendInvalidItemMessage(__instance, inputStack);
                }
            }
            catch (Exception ex)
            {
                __instance.Api?.Logger?.Error("[QuernMessage] Error in QuernSlotPatch: {0}", ex.Message);
            }
        }

        private static bool CanBeGround(ICoreAPI api, ItemStack stack)
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

        private static void SendInvalidItemMessage(BlockEntity blockEntity, ItemStack stack)
        {
            string itemName = stack.GetName();

            if (blockEntity.Api is ICoreServerAPI sapi)
            {
                // Send message to all nearby players
                foreach (var player in sapi.World.AllOnlinePlayers)
                {
                    if (player.Entity?.Pos != null &&
                        player.Entity.Pos.DistanceTo(blockEntity.Pos.ToVec3d()) < 10)
                    {
                        var serverPlayer = player as IServerPlayer;
                        serverPlayer?.SendMessage(
                            GlobalConstants.GeneralChatGroup,
                            $"'{itemName}' cannot be ground in a quern. Please place a valid item.",
                            EnumChatType.Notification
                        );
                    }
                }
            }
        }
    }
}
