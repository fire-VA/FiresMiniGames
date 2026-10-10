using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using FiresCore.Sync;
using HarmonyLib;
using UnityEngine;

namespace FiresMiniGames
{
    /// <summary>
    /// FiresMiniGames — the arcade + table-games addon. Requires FiresUnifiedCore. Ships the machine/board prefabs
    /// (roulette wheel, slot machine, arcade cabinet; later the poker/chess/fanorona sets migrating over from
    /// FiresRPGmaker) in the embedded <c>firesminigames</c> bundle and registers them with ZNetScene + the hammer.
    /// Game drivers live under Modules/ as they come online.
    /// </summary>
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.Fire.FiresUnifiedCore", BepInDependency.DependencyFlags.HardDependency)]
    public class FiresMiniGames : BaseUnityPlugin
    {
        public const string PluginGUID = "com.Fire.FiresMiniGames";
        public const string PluginName = "FiresMiniGames";
        public const string PluginVersion = "0.0.50";

        public static FiresMiniGames Instance { get; private set; }
        internal static BepInEx.Logging.ManualLogSource Log;
        public static readonly Harmony harmony = new Harmony(PluginGUID);

        public static AssetBundle assetBundle;
        public static AssetBundle gameRoomBundle;   // loose vaitems_minigames (game-room furniture/decor + HD room)
        /// <summary>Buildable prefabs queued for ZNetScene + hammer registration (see PieceRegistration).</summary>
        public static readonly List<GameObject> piecePrefabs = new List<GameObject>();
        /// <summary>Held ItemDrop prefabs queued for ZNetScene + ObjectDB.m_items registration (the game cartridges).</summary>
        public static readonly List<GameObject> itemPrefabs = new List<GameObject>();
        public static GameObject RootObject { get; private set; }

        public ConfigSync configSync;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            FiresMiniGamesBanner.PrintBig();
            Debug.Log($"[{PluginName}] Awake() entered — version {PluginVersion}");

            RootObject = new GameObject("_FiresMiniGamesRoot");
            UnityEngine.Object.DontDestroyOnLoad(RootObject);

            configSync = new ConfigSync(PluginGUID)
            {
                DisplayName = PluginName,
                CurrentVersion = PluginVersion,
                MinimumRequiredVersion = PluginVersion
            };

            Modules.Common.MiniGamesConfig.Bind(Config);
            Modules.Nes.RomLinks.Bind(Config);
            LoadAssets();
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            try { Modules.Common.MiniGamesHelpContent.Register(); }
            catch (System.Exception ex) { Debug.LogWarning($"[{PluginName}] Help registration failed: {ex.Message}"); }

            Debug.Log($"[{PluginName}] ready — {piecePrefabs.Count} piece prefab(s) queued.");
        }

        private void LoadAssets()
        {
            assetBundle = GetAssetBundleFromResources("firesminigames");
            if (assetBundle != null) Modules.Machines.MachinePieces.Load(assetBundle);
            else Debug.Log($"[{PluginName}] no embedded 'firesminigames' bundle yet — running codeless.");

            // game-room furniture/decor bundle (curated HD room) — now embedded in the DLL
            gameRoomBundle = GetAssetBundleFromResources("vaitems_minigames");
            if (gameRoomBundle != null) Modules.GameRoom.GameRoomPieces.Load(gameRoomBundle);
        }

        // Embedded-resource bundle load — LoadFromMemory ONLY (LoadFromStream crashes the dedicated server).
        private static AssetBundle GetAssetBundleFromResources(string fileName)
        {
            var asm = Assembly.GetExecutingAssembly();
            string resourceName = asm.GetManifestResourceNames().FirstOrDefault(s => s.EndsWith(fileName));
            if (resourceName == null) return null;
            using (var stream = asm.GetManifestResourceStream(resourceName))
            {
                if (stream == null) return null;
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    return AssetBundle.LoadFromMemory(ms.ToArray());
                }
            }
        }

        private void OnDestroy()
        {
            try { assetBundle?.Unload(false); } catch { }
            try { harmony.UnpatchSelf(); } catch { }
        }
    }
}
