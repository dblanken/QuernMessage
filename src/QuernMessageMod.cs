using HarmonyLib;
using System;
using System.Collections.Generic;
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
                // Patch OnSlotModified
                var onSlotModifiedMethod = quernType.GetMethod("OnSlotModified",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                if (onSlotModifiedMethod != null)
                {
                    var postfixMethod = typeof(QuernSlotPatch).GetMethod("Postfix");
                    harmony.Patch(onSlotModifiedMethod, postfix: new HarmonyMethod(postfixMethod));
                    api.Logger.Notification("[QuernMessage] Successfully patched BlockEntityQuern.OnSlotModified");
                }
                else
                {
                    api.Logger.Warning("[QuernMessage] Could not find OnSlotModified method on BlockEntityQuern");
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
                api.Logger.Notification($"[QuernMessage] Checking {stack.GetName()}");
                api.Logger.Notification($"[QuernMessage]   Has ItemAttributes: {stack.ItemAttributes != null}");

                var collectibleType = stack.Collectible?.GetType();
                if (collectibleType != null)
                {
                    var grindingProps = collectibleType.GetProperty("GrindingProps", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (grindingProps != null)
                    {
                        var props = grindingProps.GetValue(stack.Collectible);
                        if (props != null)
                        {
                            api.Logger.Notification($"[QuernMessage] Found GrindingProps object on {stack.GetName()}, type: {props.GetType().Name}");

                            var groundStackProp = props.GetType().GetProperty("GroundStack");
                            if (groundStackProp != null)
                            {
                                var groundStack = groundStackProp.GetValue(props);
                                if (groundStack != null)
                                {
                                    api.Logger.Notification($"[QuernMessage] {stack.GetName()} CAN be ground (has GroundStack)");
                                    return true;
                                }
                            }
                        }
                    }
                }

                api.Logger.Notification($"[QuernMessage] {stack.GetName()} CANNOT be ground (no valid grinding properties found)");
                return false;
            }
            catch (Exception ex)
            {
                api?.Logger?.Error("[QuernMessage] Error checking if item can be ground: {0}", ex.Message);
                api?.Logger?.Error("[QuernMessage] Stack trace: {0}", ex.StackTrace);
            }

            return false;
        }
    }

    public class MessageDeduplicator
    {
        private readonly Dictionary<BlockPos, (string itemName, long timestamp)> _lastMessages = new();
        private readonly long _deduplicationWindowMs;

        public MessageDeduplicator(long deduplicationWindowMs = 500)
        {
            _deduplicationWindowMs = deduplicationWindowMs;
        }

        public bool ShouldSend(BlockPos pos, string itemName, long currentTimeMs)
        {
            if (_lastMessages.TryGetValue(pos, out var lastMsg))
            {
                if (lastMsg.itemName == itemName && (currentTimeMs - lastMsg.timestamp) < _deduplicationWindowMs)
                {
                    return false;
                }
            }

            _lastMessages[pos] = (itemName, currentTimeMs);
            return true;
        }

        public void Clear()
        {
            _lastMessages.Clear();
        }
    }

    public interface IMessageSender
    {
        void SendToNearbyPlayers(BlockPos pos, string message);
    }

    public class ServerMessageSender : IMessageSender
    {
        private readonly ICoreServerAPI _sapi;

        public ServerMessageSender(ICoreServerAPI sapi)
        {
            _sapi = sapi;
        }

        public void SendToNearbyPlayers(BlockPos pos, string message)
        {
            foreach (var player in _sapi.World.AllOnlinePlayers)
            {
                if (player.Entity?.Pos != null &&
                    player.Entity.Pos.DistanceTo(pos.ToVec3d()) < 10)
                {
                    var serverPlayer = player as IServerPlayer;
                    serverPlayer?.SendMessage(
                        GlobalConstants.GeneralChatGroup,
                        message,
                        EnumChatType.Notification
                    );
                }
            }
        }
    }

    public static class QuernMessageHandler
    {
        private static MessageDeduplicator _deduplicator = new();
        private static IMessageSender _messageSender;

        // For testing: allow injecting dependencies
        public static void Configure(MessageDeduplicator deduplicator, IMessageSender messageSender)
        {
            _deduplicator = deduplicator;
            _messageSender = messageSender;
        }

        public static void ResetToDefaults()
        {
            _deduplicator = new MessageDeduplicator();
            _messageSender = null;
        }

        public static void SendInvalidItemMessage(BlockEntity blockEntity, ItemStack stack)
        {
            if (!(blockEntity.Api is ICoreServerAPI sapi)) return;

            string itemName = stack.GetName();
            long currentTime = sapi.World.ElapsedMilliseconds;

            if (!_deduplicator.ShouldSend(blockEntity.Pos, itemName, currentTime))
            {
                return;
            }

            string errorMessage = $"'{itemName}' cannot be ground in a quern.";

            var sender = _messageSender ?? new ServerMessageSender(sapi);
            sender.SendToNearbyPlayers(blockEntity.Pos, errorMessage);
        }
    }

    public class QuernSlotPatch
    {
        public static void Postfix(BlockEntity __instance, int slotid)
        {
            try
            {
                if (slotid != 0) return;

                var inventory = __instance.GetType()
                    .GetProperty("Inventory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(__instance) as InventoryBase;

                if (inventory == null || inventory.Count == 0) return;

                var slot = inventory[0];
                if (slot == null || slot.Empty) return;

                ItemStack inputStack = slot.Itemstack;
                if (inputStack == null) return;

                var canGrindMethod = __instance.GetType().GetMethod("CanGrind", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (canGrindMethod != null)
                {
                    bool canGrind = (bool)canGrindMethod.Invoke(__instance, null);

                    if (!canGrind)
                    {
                        QuernMessageHandler.SendInvalidItemMessage(__instance, inputStack);
                    }
                }
            }
            catch (Exception ex)
            {
                __instance.Api?.Logger?.Error("[QuernMessage] Error in QuernSlotPatch: {0}", ex.Message);
            }
        }
    }

    public class QuernRightClickPatch
    {
        public static void Postfix(BlockEntity __instance, IPlayer byPlayer, BlockSelection blockSel, ref bool __result)
        {
            try
            {
                if (!(__instance.Api is ICoreServerAPI sapi)) return;

                var inventoryProp = __instance.GetType()
                    .GetProperty("Inventory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (inventoryProp == null) return;

                var inventory = inventoryProp.GetValue(__instance) as InventoryBase;
                if (inventory == null || inventory.Count == 0) return;

                var inputSlot = inventory[0];
                if (inputSlot == null || inputSlot.Empty) return;

                ItemStack inputStack = inputSlot.Itemstack;
                if (inputStack == null) return;

                var canGrindMethod = __instance.GetType().GetMethod("CanGrind", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (canGrindMethod != null)
                {
                    bool canGrind = (bool)canGrindMethod.Invoke(__instance, null);

                    if (!canGrind)
                    {
                        QuernMessageHandler.SendInvalidItemMessage(__instance, inputStack);
                    }
                }
            }
            catch (Exception ex)
            {
                __instance.Api?.Logger?.Error("[QuernMessage] Error in QuernRightClickPatch: {0}", ex.Message);
            }
        }
    }
}
