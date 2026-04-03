using System.Reflection;
using UnityEngine;
using BepInEx;
using HarmonyLib;
using LethalLib.Modules;
using static LethalLib.Modules.Levels;
using static LethalLib.Modules.Enemies;
using BepInEx.Logging;
using System.IO;
using BepInEx.Configuration;
using LethalConfig.ConfigItems;
using LethalConfig.ConfigItems.Options;
using LethalConfig;
using MoaiEnemy.src.MoaiNormal;
using System.Collections.Generic;
using MoaiEnemy.src.Utilities;
using System;

namespace MoaiEnemy
{
    [BepInDependency(LethalLib.Plugin.ModGUID)]
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static Harmony _harmony;
        public static EnemyType ExampleEnemy;
        public static new ManualLogSource Logger;

        public static MoaiNormalNet networkHandler = new MoaiNormalNet();

        // defined assets
        public static EnemyType MoaiEnemy;
        public static TerminalNode tlTerminalNode;
        public static TerminalKeyword tlTerminalKeyword;

        public static EnemyType MoaiBlue;
        public static TerminalNode MoaiBlueTerminalNode;
        public static TerminalKeyword MoaiBlueTerminalKeyword;

        public static EnemyType MoaiRed;
        public static TerminalNode MoaiRedTerminalNode;
        public static TerminalKeyword MoaiRedTerminalKeyword;

        public static EnemyType MoaiGreen;
        public static TerminalNode MoaiGreenTerminalNode;
        public static TerminalKeyword MoaiGreenTerminalKeyword;
        public static GameObject plasmaProjectile;
        public static GameObject consumptionCircle;

        public static EnemyType MoaiGold;
        public static TerminalNode MoaiGoldTerminalNode;
        public static TerminalKeyword MoaiGoldTerminalKeyword;

        public static EnemyType MoaiPurple;
        public static TerminalNode MoaiPurpleTerminalNode;
        public static TerminalKeyword MoaiPurpleTerminalKeyword;
        public static GameObject PlasmaPad;

        public static EnemyType MoaiOrange;
        public static TerminalNode MoaiOrangeTerminalNode;
        public static TerminalKeyword MoaiOrangeTerminalKeyword;

        public static EnemyType SoulDevourer;
        public static TerminalNode SoulDevourerTerminalNode;
        public static TerminalKeyword SoulDevourerTerminalKeyword;

        public static float rawSpawnMultiplier = 0f;


        public static void LogDebug(string text)
        {
#if DEBUG
            Plugin.Logger.LogInfo(text);
#endif
        }

        public static void LogProduction(string text)
        {
            Plugin.Logger.LogInfo(text);
        }

        private void Awake()
        {
            Logger = base.Logger;
            Assets.PopulateAssets();
            bindVars();

            // Required by https://github.com/EvaisaDev/UnityNetcodePatcher
            var types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (var method in methods)
                {
                    var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }

            // asset loading phase
            MoaiEnemy = Assets.MainAssetBundle.LoadAsset<EnemyType>("MoaiEnemy");
            tlTerminalNode = Assets.MainAssetBundle.LoadAsset<TerminalNode>("MoaiEnemyTN");
            tlTerminalKeyword = Assets.MainAssetBundle.LoadAsset<TerminalKeyword>("MoaiEnemyTK");

            MoaiBlue = Assets.MainAssetBundle.LoadAsset<EnemyType>("MoaiBlue");
            MoaiBlueTerminalNode = Assets.MainAssetBundle.LoadAsset<TerminalNode>("MoaiBlueTN");
            MoaiBlueTerminalKeyword = Assets.MainAssetBundle.LoadAsset<TerminalKeyword>("MoaiBlueTK");

            MoaiRed = Assets.MainAssetBundle.LoadAsset<EnemyType>("MoaiRed");
            MoaiRedTerminalNode = Assets.MainAssetBundle.LoadAsset<TerminalNode>("MoaiRedTN");
            MoaiRedTerminalKeyword = Assets.MainAssetBundle.LoadAsset<TerminalKeyword>("MoaiRedTK");

            MoaiGreen = Assets.MainAssetBundle.LoadAsset<EnemyType>("MoaiGreen");
            MoaiGreenTerminalNode = Assets.MainAssetBundle.LoadAsset<TerminalNode>("MoaiGreenTN");
            MoaiGreenTerminalKeyword = Assets.MainAssetBundle.LoadAsset<TerminalKeyword>("MoaiGreenTK");

            MoaiPurple = Assets.MainAssetBundle.LoadAsset<EnemyType>("MoaiPurple");
            MoaiPurpleTerminalNode = Assets.MainAssetBundle.LoadAsset<TerminalNode>("MoaiGoldTN");
            MoaiPurpleTerminalKeyword = Assets.MainAssetBundle.LoadAsset<TerminalKeyword>("MoaiGoldTK");
            PlasmaPad = Assets.MainAssetBundle.LoadAsset<GameObject>("PlasmaPadPrefab");

            MoaiGold = Assets.MainAssetBundle.LoadAsset<EnemyType>("MoaiGold");
            MoaiGoldTerminalNode = Assets.MainAssetBundle.LoadAsset<TerminalNode>("MoaiGoldTN");
            MoaiGoldTerminalKeyword = Assets.MainAssetBundle.LoadAsset<TerminalKeyword>("MoaiGoldTK");

            MoaiOrange = Assets.MainAssetBundle.LoadAsset<EnemyType>("MoaiOrange");
            MoaiOrangeTerminalNode = Assets.MainAssetBundle.LoadAsset<TerminalNode>("MoaiOrangeTN");
            MoaiOrangeTerminalKeyword = Assets.MainAssetBundle.LoadAsset<TerminalKeyword>("MoaiOrangeTK");

            SoulDevourer = Assets.MainAssetBundle.LoadAsset<EnemyType>("SoulDev");
            SoulDevourerTerminalNode = Assets.MainAssetBundle.LoadAsset<TerminalNode>("SoulDevTN");
            SoulDevourerTerminalKeyword = Assets.MainAssetBundle.LoadAsset<TerminalKeyword>("SoulDevTK");

            plasmaProjectile = Assets.MainAssetBundle.LoadAsset<GameObject>("PlasmaBall01");
            consumptionCircle = Assets.MainAssetBundle.LoadAsset<GameObject>("ConsumptionCircle");

            // debug phase
            Debug.Log("MOAI ENEMY BUNDLE: " + Assets.MainAssetBundle.ToString());
            Debug.Log("Orange E: " + MoaiOrange);
            Debug.Log("SD TN: " + MoaiOrangeTerminalNode);
            Debug.Log("SD TK: " + MoaiOrangeTerminalKeyword);

            UnityEngine.Random.InitState((int)System.DateTime.Now.Ticks);

            // register phase 
            NetworkPrefabs.RegisterNetworkPrefab(MoaiEnemy.enemyPrefab);
            NetworkPrefabs.RegisterNetworkPrefab(MoaiBlue.enemyPrefab);
            NetworkPrefabs.RegisterNetworkPrefab(MoaiRed.enemyPrefab);
            NetworkPrefabs.RegisterNetworkPrefab(MoaiGreen.enemyPrefab);
            NetworkPrefabs.RegisterNetworkPrefab(MoaiGold.enemyPrefab);
            NetworkPrefabs.RegisterNetworkPrefab(MoaiPurple.enemyPrefab);
            NetworkPrefabs.RegisterNetworkPrefab(MoaiOrange.enemyPrefab);
            NetworkPrefabs.RegisterNetworkPrefab(SoulDevourer.enemyPrefab);
            NetworkPrefabs.RegisterNetworkPrefab(plasmaProjectile);
            NetworkPrefabs.RegisterNetworkPrefab(consumptionCircle);
            NetworkPrefabs.RegisterNetworkPrefab(PlasmaPad);

            // rarity range is 0-100 normally
            rawSpawnMultiplier = RawspawnHandler.getSpawnMultiplier();
            RegisterEnemy(MoaiEnemy, (int)(0), LevelTypes.All, SpawnType.Daytime, tlTerminalNode, tlTerminalKeyword);
            RegisterEnemy(MoaiBlue, (int)(0), LevelTypes.All, SpawnType.Daytime, MoaiBlueTerminalNode, MoaiBlueTerminalKeyword);
            RegisterEnemy(MoaiRed, (int)(0), LevelTypes.All, SpawnType.Daytime, MoaiRedTerminalNode, MoaiRedTerminalKeyword);
            RegisterEnemy(MoaiGreen, (int)(0), LevelTypes.All, SpawnType.Daytime, MoaiGreenTerminalNode, MoaiGreenTerminalKeyword);
            RegisterEnemy(MoaiGold, (int)(0), LevelTypes.All, SpawnType.Daytime, MoaiGreenTerminalNode, MoaiGreenTerminalKeyword);
            RegisterEnemy(MoaiPurple, (int)(0), LevelTypes.All, SpawnType.Daytime, MoaiGreenTerminalNode, MoaiGreenTerminalKeyword);
            RegisterEnemy(MoaiOrange, (int)(0), LevelTypes.All, SpawnType.Daytime, MoaiOrangeTerminalNode, MoaiOrangeTerminalKeyword);
            RegisterEnemy(SoulDevourer, (int)(0), LevelTypes.All, SpawnType.Outside, MoaiGreenTerminalNode, MoaiGreenTerminalKeyword);

            Debug.Log("MOAI: Registering Moai Net Messages");

            // actual logic for setting rarity
            On.RoundManager.LoadNewLevel += (On.RoundManager.orig_LoadNewLevel orig, global::RoundManager self, int randomSeed, global::SelectableLevel newLevel) =>
            {
                if (newLevel.PlanetName.Contains("Easter"))
                {
                    rawSpawnMultiplier = RawspawnHandler.getSpawnMultiplier(true);
                }
                else
                {
                    rawSpawnMultiplier = RawspawnHandler.getSpawnMultiplier();
                }

                var normPkg = new RawspawnHandler.enemyRarityPkg();
                normPkg.name = MoaiEnemy.name;
                normPkg.rarity = (int)(58 * baseRarity.Value * rawSpawnMultiplier);

                var greenPkg = new RawspawnHandler.enemyRarityPkg();
                greenPkg.name = MoaiGreen.name;
                greenPkg.rarity = (int)(30 * greenRarity.Value * rawSpawnMultiplier);

                var purplePkg = new RawspawnHandler.enemyRarityPkg();
                purplePkg.name = MoaiPurple.name;
                purplePkg.rarity = (int)(35 * purpleRarity.Value * rawSpawnMultiplier);

                var bluePkg = new RawspawnHandler.enemyRarityPkg();
                bluePkg.name = MoaiBlue.name;
                bluePkg.rarity = (int)(20 * blueRarity.Value * rawSpawnMultiplier);

                var redPkg = new RawspawnHandler.enemyRarityPkg();
                redPkg.name = MoaiRed.name;
                redPkg.rarity = (int)(40 * redRarity.Value * rawSpawnMultiplier);

                var orangePkg = new RawspawnHandler.enemyRarityPkg();
                orangePkg.name = MoaiOrange.name;
                orangePkg.rarity = (int)(22 * orangeRarity.Value * rawSpawnMultiplier);

                var goldPkg = new RawspawnHandler.enemyRarityPkg();
                goldPkg.name = MoaiGold.name;
                goldPkg.rarity = (int)(4 * goldRarity.Value * rawSpawnMultiplier);

                RawspawnHandler.setLevelSpawnWeights([normPkg, goldPkg, bluePkg, redPkg, greenPkg, purplePkg, orangePkg], []);

                orig.Invoke(self, randomSeed, newLevel);

                try
                {
                    GreenEnemyAI.getMapObjects();
                    GreenEnemyAI.findTraps();
                    EntityWarp.mapEntrances = UnityEngine.Object.FindObjectsOfType<EntranceTeleport>(false);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Moai Enemy: Error during map initialization process. " + e.ToString());
                }
            };

            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }
        

        // SETTINGS SECTION
        // consider these multipliers for existing values
        public static ConfigEntry<float> moaiGlobalSize;
        public static ConfigEntry<float> moaiGlobalSizeVar;
        public static ConfigEntry<float> moaiAngelChance;
        public static ConfigEntry<float> moaiSizeCap;
        public static ConfigEntry<float> moaiGlobalMusicVol;
        public static ConfigEntry<float> moaiGlobalSpeed;
        public static ConfigEntry<bool> moaiConsumeScrap;
        public static ConfigEntry<string> moaiSpawnDistribution;
        public static ConfigEntry<float> baseRarity;
        public static ConfigEntry<float> blueRarity;
        public static ConfigEntry<float> redRarity;
        public static ConfigEntry<float> greenRarity;
        public static ConfigEntry<float> purpleRarity;
        public static ConfigEntry<float> goldRarity;
        public static ConfigEntry<float> orangeRarity;
        public static ConfigEntry<float> soulRarity;
        public static ConfigEntry<float> simpleSpawnMultiplier;

        public void bindVars()
        {
            simpleSpawnMultiplier = Config.Bind("Global", "Simple Spawn Multiplier", 1f, "(Recommended) Simplest way to affect spawnrates for all moais. Multiplier to spawnrate and chance for moais to be spawnable on a day");
            moaiGlobalMusicVol = Config.Bind("Global", "Enemy Sound Volume", 0.6f, "Changes the volume of all moai sounds. May make moai more sneaky as well.");
            moaiGlobalSizeVar = Config.Bind("Global", "Size Variant Chance", 0.2f, "The chance of a moai to spawn in a randomly scaled size. Affects their pitch too.");
            moaiAngelChance = Config.Bind("Global", "Angel Spawn Chance", 0.1f, "Chance for the moai to spawn as an angel (good guy). They guard players and kill enemies, but they certainly aren't friendly forever.");
            moaiGlobalSize = Config.Bind("Global", "Size Multiplier", 1f, "Changes the size of all moai models. Scales pretty violently. Affects SFX pitch.");
            moaiSizeCap = Config.Bind("Advanced", "Size Variant Cap", 100f, "Caps the max size of a moai with the size variant. Normal size is 1. 1.5 is slightly taller than the ship. 2 is very large. 3.5+ is giant tier (with 5 being the largest usually)");
            moaiGlobalSpeed = Config.Bind("Global", "Enemy Speed Multiplier", 1f, "Changes the speed of all moai. 4x would mean they are 4 times faster, 0.5x would be 2 times slower.");
            moaiConsumeScrap = Config.Bind("Global", "Allow Scrap Eating Behavior", true, "If enabled, moai can seek out scrap and consume it. Moai that consume scrap become angel variants for a time. Does not apply to dead bodies.");
            moaiSpawnDistribution = Config.Bind("Advanced", "Enemy Spawn Distribution", "8%150%, 12%75%, 25%50%", "For fine tuning spawn multipliers day to day. Value is a comma separated list. Each value follows the format C%M%, with C being the chance for the spawnrate multiplier to activate on a day (0-100%) and M being the multiplier (0-inf%). If a multiplier isn't activated, the spawnrate will be 0%.\n\n I recommend not using SimpleSpawnMultiplier if you are editing this");
            baseRarity = Config.Bind("Variants", "Basic Moai Spawnrate", 1f, "Changes the spawnrate of the variant.");
            blueRarity = Config.Bind("Variants", "Blue Moai Spawnrate", 1f, "Changes the spawnrate of the variant.");
            redRarity = Config.Bind("Variants", "Red Moai Spawnrate", 1f, "Changes the spawnrate of the variant.");
            greenRarity = Config.Bind("Variants", "Green Moai Spawnrate", 1f, "Changes the spawnrate of the variant.");
            purpleRarity = Config.Bind("Variants", "Purple Moai Spawnrate", 1f, "Changes the spawnrate of the variant.");
            goldRarity = Config.Bind("Variants", "Gold Moai Spawnrate", 1f, "Changes the spawnrate of the variant.");
            orangeRarity = Config.Bind("Variants", "Orange Moai Spawnrate", 1f, "Changes the spawnrate of the variant.");
            soulRarity = Config.Bind("Variants", "Devourer Spawnrate", 0.4f, "Changes the spawnrate of this... thing. Note that devourers don't spawn naturally, they have a chance to spawn when any moai consumes a corpse.");

            var angelSlider = new FloatSliderConfigItem(moaiAngelChance, new FloatSliderOptions
            {
                RequiresRestart = false,
                Min = 0f,
                Max = 1f
            });

            var sizeSlider = new FloatSliderConfigItem(moaiGlobalSize, new FloatSliderOptions
            {
                RequiresRestart = false,
                Min = 0.05f,
                Max = 5f
            });

            var sizeVarSlider = new FloatSliderConfigItem(moaiGlobalSizeVar, new FloatSliderOptions
            {
                RequiresRestart = false,
                Min = 0f,
                Max = 1f
            });

            var maxSizeEntry = new FloatInputFieldConfigItem(moaiSizeCap, new FloatInputFieldOptions
            {
                RequiresRestart = false,
                Min = 0.01f,
                Max = 100f,
            });


            var volumeSlider = new FloatSliderConfigItem(moaiGlobalMusicVol, new FloatSliderOptions
            {
                RequiresRestart = false,
                Min = 0.0f,
                Max = 1f
            });

            var speedSlider = new FloatSliderConfigItem(moaiGlobalSpeed, new FloatSliderOptions
            {
                RequiresRestart = false,
                Min = 0.0f,
                Max = 5f,
            });

            var moaiConsumeScrapEntry = new BoolCheckBoxConfigItem(moaiConsumeScrap, new BoolCheckBoxOptions
            {
                RequiresRestart = false,
            });

            var spawnEntry = new TextInputFieldConfigItem(moaiSpawnDistribution, new TextInputFieldOptions
            {
                RequiresRestart = false,
            });

            var baseEntry = new FloatInputFieldConfigItem(baseRarity, new FloatInputFieldOptions
            {
                RequiresRestart = false,
                Min = 0.0f,
                Max = 10000f,
            });


            var greenEntry = new FloatInputFieldConfigItem(greenRarity, new FloatInputFieldOptions
            {
                RequiresRestart = false,
                Min = 0.0f,
                Max = 10000f,
            });

            var purpleEntry = new FloatInputFieldConfigItem(purpleRarity, new FloatInputFieldOptions
            {
                RequiresRestart = false,
                Min = 0.0f,
                Max = 10000f,
            });

            var redEntry = new FloatInputFieldConfigItem(redRarity, new FloatInputFieldOptions
            {
                RequiresRestart = false,
                Min = 0.0f,
                Max = 10000f,
            });

            var blueEntry = new FloatInputFieldConfigItem(blueRarity, new FloatInputFieldOptions
            {
                RequiresRestart = false,
                Min = 0.0f,
                Max = 10000f,
            });

            var goldEntry = new FloatInputFieldConfigItem(goldRarity, new FloatInputFieldOptions
            {
                RequiresRestart = false,
                Min = 0.0f,
                Max = 10000f,
            });

            var orangeEntry = new FloatInputFieldConfigItem(orangeRarity, new FloatInputFieldOptions
            {
                RequiresRestart = false,
                Min = 0.0f,
                Max = 10000f,
            });

            var devourerEntry = new FloatInputFieldConfigItem(goldRarity, new FloatInputFieldOptions
            {
                RequiresRestart = false,
                Min = 0.0f,
                Max = 10000f,
            });

            var simpleEntry = new FloatInputFieldConfigItem(simpleSpawnMultiplier, new FloatInputFieldOptions
            {
                RequiresRestart = false,
                Min = 0.0f,
                Max = 10000f,
            });

            LethalConfigManager.AddConfigItem(simpleEntry);
            LethalConfigManager.AddConfigItem(volumeSlider);
            LethalConfigManager.AddConfigItem(sizeSlider);
            LethalConfigManager.AddConfigItem(angelSlider);
            LethalConfigManager.AddConfigItem(sizeVarSlider);
            LethalConfigManager.AddConfigItem(speedSlider);
            LethalConfigManager.AddConfigItem(moaiConsumeScrapEntry);
            LethalConfigManager.AddConfigItem(baseEntry);
            LethalConfigManager.AddConfigItem(blueEntry);
            LethalConfigManager.AddConfigItem(redEntry);
            LethalConfigManager.AddConfigItem(spawnEntry);
            LethalConfigManager.AddConfigItem(greenEntry);
            LethalConfigManager.AddConfigItem(purpleEntry);
            LethalConfigManager.AddConfigItem(goldEntry);
            LethalConfigManager.AddConfigItem(orangeEntry);
            LethalConfigManager.AddConfigItem(devourerEntry);
            LethalConfigManager.AddConfigItem(maxSizeEntry);
        }

        public static class Assets
        {
            public static AssetBundle MainAssetBundle = null;
            public static void PopulateAssets()
            {
                string sAssemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

                MainAssetBundle = AssetBundle.LoadFromFile(Path.Combine(sAssemblyLocation, "moaibundle"));

                if (MainAssetBundle == null)
                {
                    Plugin.Logger.LogError("Failed to load custom assets.");
                    return;
                }
            }
        }
    }
}