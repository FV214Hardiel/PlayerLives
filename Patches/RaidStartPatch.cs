using SPT.Reflection.Patching;
using EFT;
using HarmonyLib;
using System.Reflection;
using UnityEngine.SceneManagement;
using PlayerLives.Helpers;

namespace PlayerLives.Patches
{
    public class RaidStartPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted));
        }


        [PatchPostfix]
        private static void PatchPostfix(GameWorld __instance)
        {
            var gameWorld = __instance;
            if (gameWorld == null || gameWorld.MainPlayer == null || IsInHideout()) return;

            // Raid start, reset number of lives 
            Plugin.CurrentLives = Settings.PLAYER_LIVES.Value;
            Plugin.Cooldown = Settings.REVIVE_ON_CD_AT_THE_BEGINNING.Value ? Settings.REVIVE_COOLDOWN.Value : 0;
            Plugin.GaveUp = false;
            Plugin.shownDeathNotification = false;

            Plugin.LogSource.LogInfo($"Raid started, setting lives to {Plugin.CurrentLives}");
        }

        private static bool IsInHideout()
        {
            // Check if "bunker_2" is one of the active scene names
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.name == "bunker_2")
                {
                    //EFT.UI.ConsoleScreen.LogError("bunker_2 loaded, not running de-cluttering.");
                    return true;
                }
            }
            //EFT.UI.ConsoleScreen.LogError("bunker_2 not loaded, de-cluttering.");
            return false;
        }
    }
}