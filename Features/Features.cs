using EFT;
using EFT.HealthSystem;
using EFT.Communications;
using Comfort.Common;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using PlayerLives.Helpers;
using System.Linq;
using System.Collections;

namespace PlayerLives.Features
{
    /// <summary>
    /// Enhanced revival feature with manual activation and temporary invulnerability with restrictions
    /// </summary>
    internal class RevivalFeatures : ModulePatch
    {
        private static readonly bool DISABLE_SHOOTING_DURING_INVULNERABILITY = true; // Disable shooting during invulnerability
        // States
        private static Dictionary<string, long> _lastRevivalTimesByPlayer = new Dictionary<string, long>();
        private static Dictionary<string, bool> _playerInCriticalState = new Dictionary<string, bool>();
        private static Dictionary<string, bool> _playerIsInvulnerable = new Dictionary<string, bool>();
        private static Dictionary<string, float> _playerInvulnerabilityTimers = new Dictionary<string, float>();
        private static Dictionary<string, float> _playerDamageCoeff = new Dictionary<string, float>();
        private static Player PlayerClient { get; set; } = null;

        protected override MethodBase GetTargetMethod()
        {
            // We're patching the Update method of Player to constantly check for revival key press
            return AccessTools.Method(typeof(Player), nameof(Player.UpdateTick));
        }

        [PatchPostfix]
        static void Postfix(Player __instance)
        {
            try
            {
                string playerId = __instance.ProfileId;
                PlayerClient = __instance;

                // Only proceed for the local player
                if (!__instance.IsYourPlayer) return;

                // Update invulnerability timer if active
                if (_playerIsInvulnerable.TryGetValue(playerId, out bool isInvulnerable) && isInvulnerable)
                {
                    if (_playerInvulnerabilityTimers.TryGetValue(playerId, out float timer))
                    {
                        timer -= Time.deltaTime;
                        _playerInvulnerabilityTimers[playerId] = timer;

                        if (DISABLE_SHOOTING_DURING_INVULNERABILITY)
                        {
                            // disable shooting by setting hands to nothing
                            PlayerClient.SetEmptyHands(null);
                        }

                        // End invulnerability if timer is up
                        if (timer <= 0)
                        {
                            EndInvulnerability(__instance);
                        }
                    }
                }

                // Check for manual revival key press when in critical state
                if (_playerInCriticalState.TryGetValue(playerId, out bool inCritical) && inCritical)
                {
                    // Force critical state 
                    PlayerClient.SetEmptyHands(null);
                    PlayerClient.MovementContext.SetPoseLevel(0);
                    PlayerClient.MovementContext.IsInPronePose = true;

                    if (Input.GetKeyDown(Settings.REVIVAL_KEY.Value))
                    {
                        TryPerformManualRevival(__instance);
                    }

                    // Check for give up
                    if (Input.GetKeyDown(Settings.GIVE_UP_KEY.Value))
                    {
                        GiveUp(__instance);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error in RevivalFeatureExtension patch: {ex.Message}");
            }
        }

        public static void SetPlayerCriticalState(Player player, bool criticalState)
        {
            if (player == null)
                return;

            string playerId = player.ProfileId;

            // Update critical state
            _playerInCriticalState[playerId] = criticalState;

            if (criticalState)
            {
                // Apply effects when entering critical state
                // Make player invulnerable while in critical state
                _playerIsInvulnerable[playerId] = true;

                // Put player in down state 
                ApplyCriticalStatePlayer(player);

                if (player.IsYourPlayer)
                {
                    try
                    {
                        // Show revival message
                        NotificationManagerClass.DisplayMessageNotification(
                            $"CRITICAL CONDITION! Press {Settings.REVIVAL_KEY.Value.ToString()} to revive!",
                            ENotificationDurationType.Long,
                            ENotificationIconType.Default,
                            Color.red);
                    }
                    catch (Exception ex)
                    {
                        Plugin.LogSource.LogError($"Error displaying critical state UI: {ex.Message}");
                    }
                }
            }
            else
            {
                // If player is leaving critical state without revival (e.g., revival failed),
                // make sure to remove stealth from player and disable invulnerability
                if (!_playerInvulnerabilityTimers.ContainsKey(playerId))
                {
                    _playerIsInvulnerable.Remove(playerId);
                }
            }
        }

        public static string FindItemId(string stimName)
        {
            return stimName switch
            {
                "Adrenaline" => "5c10c8fd86f7743d7d706df3",
                "Propital" => "5c0e530286f7747fa1419862",
                "SJ1TGLabs" => "5c0e531286f7747fa54205c2",
                "SJ6TGLabs" => "5c0e531d86f7747fa23f4d42",
                "Zagustin" => "5c0e533786f7747fa23f4d47",
                "eTGchange" => "5c0e534186f7747fa1419867",
                "2A2bTG" => "66507eabf5ddb0818b085b68",
                "3bTG" => "5ed515c8d380ab312177c0fa",
                "AHF1M" => "5ed515f6915ec335206e4152",
                "Antidote" => "5ed515f6915ec335206e4152",
                "L1" => "5ed515e03a40a50460332579",
                "MULE" => "5ed51652f6c34d2cc26336a1",
                "Meldonin" => "5ed5160a87bb8443d10680b5",
                "Obdolbos" => "5ed5166ad380ab312177c100",
                "Obdolbos2" => "637b60c3b7afa97bfc3d7001",
                "P22" => "5ed515ece452db0eb56fc028",
                "PNB" => "637b6179104668754b72f8f5",
                "Perfotoran" => "637b6251104668754b72f8f9",
                "SJ12_TGLabs" => "637b612fb7afa97bfc3d7005",
                "Trimadol" => "637b620db7afa97bfc3d7009",
                _ => "",
            };

        }

        private static void ApplyCriticalStatePlayer(Player player)
        {
            try
            {
                string playerId = player.ProfileId;

                // player.SetEmptyHands(null);
                player.HandsController.IsAiming = false;
                player.MovementContext.EnableSprint(false);
                player.MovementContext.SetPoseLevel(0);
                player.MovementContext.IsInPronePose = true;

                player.ResetLookDirection();
                player.MovementContext.ReleaseDoorIfInteractingWithOne();

                // Apply black out effect on revive
                player.ActiveHealthController.DoContusion(1f, 1f);
                player.ActiveHealthController.DoStun(1f, 1f);

                // Is alive player can't open menu
                // player.ActiveHealthController.IsAlive = false;

            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error applying stealth mode: {ex.Message}");
            }
        }

        public static bool hasReviveItem(Player player)
        {
            var reviveItemId = FindItemId(Settings.REQUIRE_STIM.Value);

            if (reviveItemId == "") return false;

            var inRaidItems = player.Inventory.GetPlayerItems(EFT.InventoryLogic.EPlayerItems.Equipment);

            return inRaidItems.Any(item => item.TemplateId == reviveItemId);
        }

        private static void ConsumeReviveItem(Player player)
        {
            try
            {
                var reviveItemId = FindItemId(Settings.REQUIRE_STIM.Value);

                if (reviveItemId == "") return;

                var inRaidItems = player.Inventory.GetPlayerItems(EFT.InventoryLogic.EPlayerItems.Equipment);
                var reviveItem = inRaidItems.FirstOrDefault(item => item.TemplateId == reviveItemId);

                if (reviveItem != null)
                {
                    GStruct455<GClass3200> gStruct = InteractionsHandlerClass.Discard(reviveItem, player.InventoryController, true);
                    if (gStruct.Failed)
                    {
                        Plugin.LogSource.LogError($"Error consuming item: {gStruct.Error}");
                    }
                    else
                    {
                        player.InventoryController.vmethod_1(
                            new RemoveOperationClass(player.InventoryController.method_12(), player.InventoryController, gStruct.Value),
                            null
                            );

                        Plugin.LogSource.LogInfo($"You have {CountReviveItemsInRaid(player, reviveItemId)} revive items left");
                    }
                }


            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error consuming item: {ex.Message}");
            }
        }

        public static int CountReviveItemsInRaid(Player player, string reviveItemId)
        {
            // Initialize a counter for items matching the reviveItemId
            int count = 0;

            // Retrieve all items in raid from the player's inventory
            var inRaidItems = player.Inventory.GetPlayerItems(EFT.InventoryLogic.EPlayerItems.Equipment);

            // Iterate over the items to count matching items
            foreach (var item in inRaidItems)
            {
                if (item.TemplateId == reviveItemId)
                {
                    count++;
                }
            }

            return count;
        }

        public static bool TryPerformManualRevival(Player player)
        {
            if (player == null) return false;

            string playerId = player.ProfileId;

            if (Settings.REQUIRE_STIM.Value != "None")
                ConsumeReviveItem(player);

            HealPlayer(player);

            StartInvulnerability(player);

            // Stand player up 
            player.MovementContext.SetPoseLevel(1);
            player.MovementContext.IsInPronePose = false;
            player.MovementContext.EnableSprint(true);

            player.Say(EPhraseTrigger.OnMutter, false, 2f, ETagStatus.Combat, 100, true);

            _playerInCriticalState[playerId] = false;
            _lastRevivalTimesByPlayer[playerId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Show successful revival notification
            NotificationManagerClass.DisplayMessageNotification(
                $"Invulnerability started - {Settings.REVIVAL_DURATION.Value}s! {Plugin.CurrentLives} lives left.",
                ENotificationDurationType.Long,
                ENotificationIconType.Default,
                Color.green);

            Plugin.LogSource.LogInfo($"Manual revival performed for player {playerId}");
            return true;
        }

        public static bool GiveUp(Player player)
        {
            if (player == null) return false;

            string playerId = player.ProfileId;

            // Set alive first before applying effects
            _playerInCriticalState[playerId] = false;
            _lastRevivalTimesByPlayer[playerId] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            Plugin.GaveUp = true;

            // Kill player by applying fatal damage to head
            if (player.ActiveHealthController != null)
            {
                player
                .ActiveHealthController
                .ApplyDamage(EBodyPart.Head, player.ActiveHealthController.GetBodyPartHealth(EBodyPart.Head).Maximum + 100f, new DamageInfoStruct());
            }

            // Show successful revival notification
            NotificationManagerClass.DisplayMessageNotification(
                $"You gave up!",
                ENotificationDurationType.Long,
                ENotificationIconType.Default,
                Color.red);

            Plugin.LogSource.LogInfo($"Player gave up {playerId}");
            return true;
        }

        public static void HealPlayer(Player player)
        {
            try
            {
                ActiveHealthController healthController = player.ActiveHealthController;
                if (healthController == null)
                {
                    Plugin.LogSource.LogError("Could not get ActiveHealthController");
                    return;
                }

                foreach (EBodyPart bodyPart in Enum.GetValues(typeof(EBodyPart)))
                {
                    if (healthController.GetBodyPartHealth(bodyPart).Current < 1)
                    {
                        // Remove bleed from destroyed body parts
                        healthController.method_16(bodyPart);

                        if (Settings.RESTORE_DESTROYED_BODY_PARTS.Value)
                        {
                            if (Settings.RESTORE_ONLY_HEAD_AND_CHEST.Value)
                            {
                                if (bodyPart != EBodyPart.Head && bodyPart != EBodyPart.Chest)
                                {
                                    continue;
                                }
                            }

                            // from healthController.FullRestoreBodyPart(bodyPart);
                            // take health down to 25%
                            ActiveHealthController.BodyPartState bodyPartState = healthController.Dictionary_0[bodyPart];
                            bodyPartState.IsDestroyed = false;
                            float healingPercent = bodyPartState.Health.Maximum * (Settings.RESTORE_DESTROYED_BODY_PARTS_HEALING.Value / 100f);
                            bodyPartState.Health = new HealthValue(
                                healingPercent,
                                bodyPartState.Health.Maximum
                            );
                            healthController.method_43(bodyPart, EDamageType.Undefined);
                            healthController.method_35(bodyPart);
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"Error applying revival effects: {ex.Message}");
            }
        }

        private static void StartInvulnerability(Player player)
        {
            if (player == null) return;

            string playerId = player.ProfileId;
            _playerIsInvulnerable[playerId] = true;
            _playerInvulnerabilityTimers[playerId] = Settings.REVIVAL_DURATION.Value;

            // Apply damage reduction buff
            ActiveHealthController healthController = player.ActiveHealthController;
            _playerDamageCoeff[player.ProfileId] = healthController.DamageCoeff;
            healthController.SetDamageCoeff(0f);

            // Apply pain killer buff so you can run on damaged legs
            healthController.AddEffect<PainKiller>(EBodyPart.Head, null, Settings.REVIVAL_DURATION.Value);

            Plugin.LogSource.LogInfo($"Started invulnerability for player {playerId} for {Settings.REVIVAL_DURATION.Value} seconds");
        }

        protected class PainKiller : ActiveHealthController.GClass2813, GInterface332, IEffect, GInterface306, GInterface308, GInterface304
        {
            public string ItemTemplateId { get; set; }
            public float MaxDuration { get; set; }
            public void UpdateWithSameOne(float strength)
            {
                float num = MaxDuration * strength;
                AddWorkTime(Mathf.Clamp(base.TimeLeft + num, 0f, MaxDuration), reset: true);
            }
            public void StoreValues(string templateId, float duration)
            {
                ItemTemplateId = templateId;
                MaxDuration = duration;
            }
        }

        private static void EndInvulnerability(Player player)
        {
            if (player == null) return;

            string playerId = player.ProfileId;
            _playerIsInvulnerable[playerId] = false;
            _playerInvulnerabilityTimers.Remove(playerId);

            // Restore damageCoeff
            if (_playerDamageCoeff.TryGetValue(playerId, out float damageCoeff))
                player.ActiveHealthController.SetDamageCoeff(damageCoeff);

            NotificationManagerClass.DisplayMessageNotification(
                "Invulnerability ended!",
                ENotificationDurationType.Long,
                ENotificationIconType.Alert,
                Color.white);

            Plugin.LogSource.LogInfo($"Ended invulnerability for player {playerId}");
        }

        public static bool IsPlayerInvulnerable(string playerId)
        {
            return _playerIsInvulnerable.TryGetValue(playerId, out bool invulnerable) && invulnerable;
        }
    }
}