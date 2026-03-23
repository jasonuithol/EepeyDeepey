using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EepyDeepy
{
    public class SequenceEntry
    {
        public int    Seconds;
        public string Message;
    }

    public class EepyDeepyConfig
    {
        public List<SequenceEntry> Sequence = new List<SequenceEntry>();
    }

    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class EepyDeepyPlugin : BaseUnityPlugin
    {
        public const string PluginGUID    = "com.byawn.eepydeepy";
        public const string PluginName    = "EepyDeepy";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static EepyDeepyPlugin Instance;

        private Harmony harmony;
        private EepyDeepyConfig config;

        private string configPath;
        private FileSystemWatcher configWatcher;
        private DateTime lastConfigReload = DateTime.MinValue;
        private DateTime lastBedExit = DateTime.MinValue;
        private bool inBed = false;

        // Sequence state
        private int      sequenceIndex  = 0;
        private bool     sequenceActive = false;
        private int      playersInBed   = 0;
        private ZNetPeer lastBedPeer    = null;

        private void Awake()
        {
            Log      = Logger;
            Instance = this;

            harmony = new Harmony(PluginGUID);
            harmony.PatchAll();

            configPath = Path.Combine(Paths.ConfigPath, "eepydeepy.cfg");
            LoadConfig();
            StartConfigWatcher();

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }

        private void LoadConfig()
        {
            if (!File.Exists(configPath))
            {
                Log.LogError($"Config not found at {configPath}.");
                return;
            }

            var newConfig = new EepyDeepyConfig();

            foreach (string raw in File.ReadAllLines(configPath))
            {
                string line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                int space = line.IndexOf(' ');
                if (space < 0) continue;

                string secondsStr = line.Substring(0, space).Trim();
                string message    = line.Substring(space + 1).Trim();

                if (int.TryParse(secondsStr, out int seconds) && !string.IsNullOrEmpty(message))
                {
                    newConfig.Sequence.Add(new SequenceEntry { Seconds = seconds, Message = message });
                }
            }

            config = newConfig;
            LogConfig();
        }

        private void LogConfig()
        {
            Log.LogInfo($"Config loaded: {config.Sequence.Count} sequence entries.");
            foreach (var entry in config.Sequence)
            {
                Log.LogInfo($"  [{entry.Seconds}s] {entry.Message}");
            }
        }

        private void StartConfigWatcher()
        {
            configWatcher = new FileSystemWatcher(Paths.ConfigPath, "eepydeepy.cfg");
            configWatcher.NotifyFilter = NotifyFilters.LastWrite;
            configWatcher.Changed += OnConfigChanged;
            configWatcher.EnableRaisingEvents = true;
        }

        private void OnConfigChanged(object sender, FileSystemEventArgs e)
        {
            if ((DateTime.Now - lastConfigReload).TotalSeconds < 1) return;
            lastConfigReload = DateTime.Now;

            System.Threading.Thread.Sleep(200);
            Log.LogInfo("Config file changed, reloading...");
            LoadConfig();
        }

        public void OnPlayerBedEnter(ZNetPeer peer, string trigger)
        {
            if (inBed) return;  // already in bed, ignore
            inBed = true;

            playersInBed++;
            lastBedPeer = peer;
            Log.LogInfo($"Player {peer?.m_playerName} triggered EepyDeepy via {trigger}. Players in bed: {playersInBed}");

            if (!sequenceActive)
            {
                sequenceActive = true;
                sequenceIndex  = 0;
                Log.LogInfo($"Starting EepyDeepy sequence, triggered by {peer?.m_playerName} via {trigger}.");
                StartCoroutine(RunSequence());
            }
        }

        public void OnPlayerBedExit()
        {
            if (!inBed) return;  // not in bed, ignore all the noise
            inBed = false;

            if ((DateTime.Now - lastBedExit).TotalSeconds < 2) return;
            lastBedExit = DateTime.Now;

            playersInBed = Math.Max(0, playersInBed - 1);
            Log.LogInfo($"Player left bed. Players in bed: {playersInBed}");

            if (playersInBed == 0)
            {
                ResetSequence("all players left bed");
            }
        }

        public void OnSleepSuccess()
        {
            ResetSequence("sleep succeeded");
        }

        private void ResetSequence(string reason)
        {
            Log.LogInfo($"Resetting sequence: {reason}.");
            inBed = false;
            sequenceActive = false;
            sequenceIndex  = 0;
            playersInBed   = 0;
            lastBedPeer    = null;
            StopAllCoroutines();
        }

        private IEnumerator RunSequence()
        {
            while (sequenceActive && sequenceIndex < config.Sequence.Count)
            {
                var entry = config.Sequence[sequenceIndex];
                yield return new WaitForSeconds(entry.Seconds);

                if (!sequenceActive) yield break;

                BroadcastChatMessage(entry.Message);
                sequenceIndex++;
            }

            if (sequenceActive)
            {
                Log.LogInfo("Sequence exhausted.");
                sequenceActive = false;
            }
        }

        private void BroadcastChatMessage(string text)
        {
            if (ZNet.instance == null) return;
            if (ZRoutedRpc.instance == null) return;

            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.Everybody,
                "ShowMessage",
                (int)MessageHud.MessageType.Center,
                text
            );

            Log.LogInfo($"[EepyDeepy] {text}");
        }

        private void OnDestroy()
        {
            configWatcher?.Dispose();
            harmony?.UnpatchSelf();
        }
    }

    [HarmonyPatch(typeof(Bed), nameof(Bed.Interact))]
    public static class Patch_Bed_Interact
    {
        static void Postfix(Humanoid human, bool __result)
        {
            if (!ZNet.instance.IsServer()) return;
            if (!__result) return;

            // Find the peer that matches this humanoid
            ZNetPeer peer = null;
            foreach (var p in ZNet.instance.GetPeers())
            {
                if (p.m_playerName == human.GetHoverName())
                {
                    peer = p;
                    break;
                }
            }

            EepyDeepyPlugin.Instance.OnPlayerBedEnter(peer, "entered bed");
        }
    }

    [HarmonyPatch(typeof(Bed), "SetOwner")]
    public static class Patch_Bed_SetOwner
    {
        static void Postfix(long uid)
        {
            if (!ZNet.instance.IsServer()) return;
            if (uid == 0)
            {
                EepyDeepyPlugin.Instance.OnPlayerBedExit();
            }
        }
    }

    [HarmonyPatch(typeof(EnvMan), "SkipToMorning")]
    public static class Patch_EnvMan_SkipToMorning
    {
        static void Postfix()
        {
            if (!ZNet.instance.IsServer()) return;
            EepyDeepyPlugin.Instance.OnSleepSuccess();
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.StartEmote))]
    public static class Patch_Player_StartEmote
    {
        static void Postfix(string emote, Player __instance)
        {
            if (emote != "rest") return;

            EepyDeepyPlugin.Log.LogInfo($"Player {__instance.GetHoverName()} used rest emote, triggering sequence.");
            EepyDeepyPlugin.Instance.OnPlayerBedEnter(null, "rest emote");
        }
    }

    [HarmonyPatch(typeof(Player), "StopEmote")]
    public static class Patch_Player_StopEmote
    {
        static void Prefix(Player __instance)
        {
            if (Player.LastEmote != "rest") return;

            EepyDeepyPlugin.Log.LogInfo($"Player {__instance.GetHoverName()} stopped rest emote.");
            EepyDeepyPlugin.Instance.OnPlayerBedExit();
        }
    }

}
