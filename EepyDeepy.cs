using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

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
    [BepInDependency(Jotunn.Main.ModGuid)]
    public class EepyDeepyPlugin : BaseUnityPlugin
    {
        public const string PluginGUID    = "com.byawn.eepydeepy";
        public const string PluginName    = "EepyDeepy";
        public const string PluginVersion = "1.0.2";

        internal static ManualLogSource Log;
        internal static EepyDeepyPlugin Instance;

        private Harmony harmony;
        private EepyDeepyConfig config;

        private string configPath;
        private FileSystemWatcher configWatcher;
        private DateTime lastConfigReload = DateTime.MinValue;
        private DateTime lastBedExit      = DateTime.MinValue;
        private bool inBed = false;

        // Sequence state (server only)
        private int      sequenceIndex  = 0;
        private bool     sequenceActive = false;
        private int      playersInBed   = 0;
        private ZNetPeer lastBedPeer    = null;

        // Audio (client only)
        private AudioSource audioSource;
        private AudioClip   lullaby;

        // Jotunn RPCs
        internal CustomRPC bedEnterRPC;      // client -> server
        internal CustomRPC bedExitRPC;       // client -> server
        private CustomRPC playLullabyRPC;   // server -> all clients
        private CustomRPC stopLullabyRPC;   // server -> all clients

        private void Awake()
        {
            Log      = Logger;
            Instance = this;

            harmony = new Harmony(PluginGUID);
            harmony.PatchAll();

            configPath = Path.Combine(Paths.ConfigPath, "eepydeepy.cfg");
            LoadConfig();
            StartConfigWatcher();

            // Only load audio on clients
            if (!GUIManager.IsHeadless())
            {
                audioSource        = gameObject.AddComponent<AudioSource>();
                audioSource.loop   = true;
                audioSource.volume = 0f;

                string audioPath = Path.Combine(
                    Path.GetDirectoryName(Info.Location),
                                                "lullaby.ogg"
                );
                StartCoroutine(LoadAudio(audioPath));
            }

            // client -> server: player entered bed/emote
            bedEnterRPC = NetworkManager.Instance.AddRPC(
                "BedEnter",
                RPC_OnBedEnter,   // server handler
                RPC_NoOp          // client handler (unused)
            );

            // client -> server: player left bed/emote
            bedExitRPC = NetworkManager.Instance.AddRPC(
                "BedExit",
                RPC_OnBedExit,    // server handler
                RPC_NoOp          // client handler (unused)
            );

            // server -> all clients: play lullaby
            playLullabyRPC = NetworkManager.Instance.AddRPC(
                "PlayLullaby",
                RPC_NoOp,
                RPC_OnPlayLullaby
            );

            // server -> all clients: stop lullaby
            stopLullabyRPC = NetworkManager.Instance.AddRPC(
                "StopLullaby",
                RPC_NoOp,
                RPC_OnStopLullaby
            );

            Log.LogInfo($"{PluginName} v{PluginVersion} loaded.");
        }

        // ---- RPC handlers ----

        private IEnumerator RPC_NoOp(long sender, ZPackage package)
        {
            yield break;
        }

        // Server receives: a player got into bed or did /rest
        private IEnumerator RPC_OnBedEnter(long sender, ZPackage package)
        {
            Log.LogInfo($"RPC_OnBedEnter received from {sender}.");

            ZNetPeer peer = ZNet.instance.GetPeer(sender);
            OnPlayerBedEnter(peer, "RPC");
            yield break;
        }

        // Server receives: a player left bed or stopped /rest
        private IEnumerator RPC_OnBedExit(long sender, ZPackage package)
        {
            Log.LogInfo($"RPC_OnBedExit received from {sender}.");
            OnPlayerBedExit();
            yield break;
        }

        // Client receives: start playing lullaby
        private IEnumerator RPC_OnPlayLullaby(long sender, ZPackage package)
        {
            Log.LogInfo($"RPC_OnPlayLullaby received. lullaby={lullaby != null} audioSource={audioSource != null}");
            StartMusic();
            yield break;
        }

        // Client receives: stop playing lullaby
        private IEnumerator RPC_OnStopLullaby(long sender, ZPackage package)
        {
            Log.LogInfo("RPC_OnStopLullaby received.");
            StopAllCoroutines();
            StartCoroutine(FadeOutMusic(3f));
            yield break;
        }

        // ---- Audio ----

        private IEnumerator LoadAudio(string path)
        {
            if (!File.Exists(path))
            {
                Log.LogWarning($"Lullaby not found at {path}. No music will play.");
                yield break;
            }

            using (var req = UnityWebRequestMultimedia.GetAudioClip("file://" + path, AudioType.OGGVORBIS))
            {
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Log.LogError($"Failed to load lullaby: {req.error}");
                    yield break;
                }

                lullaby = DownloadHandlerAudioClip.GetContent(req);
                Log.LogInfo("Lullaby loaded.");
            }
        }

        private void StartMusic()
        {
            Log.LogInfo($"StartMusic called. lullaby={lullaby != null} audioSource={audioSource != null}");
            if (lullaby == null) return;
            audioSource.clip   = lullaby;
            audioSource.volume = 0f;
            audioSource.Play();
            Log.LogInfo("Audio playing.");
            StartCoroutine(FadeInMusic(3f));
        }

        private IEnumerator FadeInMusic(float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                audioSource.volume = Mathf.Clamp01(t / duration);
                yield return null;
            }
            audioSource.volume = 1f;
        }

        private IEnumerator FadeOutMusic(float duration)
        {
            float startVolume = audioSource.volume;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                audioSource.volume = Mathf.Clamp01(startVolume * (1f - t / duration));
                yield return null;
            }
            audioSource.Stop();
            audioSource.volume = 0f;
        }

        // ---- Config ----

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

        // ---- Sequence (server only) ----

        public void OnPlayerBedEnter(ZNetPeer peer, string trigger)
        {
            if (inBed) return;
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
                playLullabyRPC.SendPackage(ZRoutedRpc.Everybody, new ZPackage());
            }
        }

        public void OnPlayerBedExit()
        {
            if (!inBed) return;
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
            inBed          = false;
            sequenceActive = false;
            sequenceIndex  = 0;
            playersInBed   = 0;
            lastBedPeer    = null;
            StopAllCoroutines();
            stopLullabyRPC.SendPackage(ZRoutedRpc.Everybody, new ZPackage());
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

    // Bed patches — still useful for dedicated server where bed interaction is server-side
    [HarmonyPatch(typeof(Bed), nameof(Bed.Interact))]
    public static class Patch_Bed_Interact
    {
        static void Postfix(Humanoid human, bool __result)
        {
            if (!ZNet.instance.IsServer()) return;
            if (!__result) return;

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

    // Emote patches — client side, send RPC to server instead of triggering directly
    [HarmonyPatch(typeof(Player), nameof(Player.StartEmote))]
    public static class Patch_Player_StartEmote
    {
        static void Postfix(string emote, Player __instance)
        {
            if (emote != "rest") return;

            EepyDeepyPlugin.Log.LogInfo($"Player {__instance.GetHoverName()} used rest emote, notifying server.");
            EepyDeepyPlugin.Instance.bedEnterRPC.SendPackage(
                ZNet.instance.GetServerPeer().m_uid,
                new ZPackage()
            );
        }
    }

    [HarmonyPatch(typeof(Player), "StopEmote")]
    public static class Patch_Player_StopEmote
    {
        static void Prefix(Player __instance)
        {
            if (Player.LastEmote != "rest") return;

            EepyDeepyPlugin.Log.LogInfo($"Player {__instance.GetHoverName()} stopped rest emote, notifying server.");
            EepyDeepyPlugin.Instance.bedExitRPC.SendPackage(
                ZNet.instance.GetServerPeer().m_uid,
                new ZPackage()
            );
        }
    }
}
