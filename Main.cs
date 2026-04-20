using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CGR {
    [BepInPlugin(ID, NAME, VERSION)]
    class Plugin : BaseUnityPlugin {
        const string ID = "51.CGRetry";
        const string NAME = "CGRetry";
        const string VERSION = "1.0.0";

        static new ManualLogSource Logger;
        static EndlessGridState endlessGridState = new EndlessGridState();
        static bool isRetrying = false;
        static bool isStartup = false;
        static bool hasCbStarted = false;
        static bool hasRefusedRetryOnDeath = false;

        void Awake() {
            Logger = base.Logger;
            ConfigManager.Initialize(Config);
            Harmony harmony = new Harmony(NAME);
            harmony.PatchAll();
            Logger.LogInfo($"{NAME} loaded");
        }

        class ConfigManager {
            public static ConfigEntry<int> retryStartRank;
            public static ConfigEntry<float> retryStartupTime;
            public static void Initialize(ConfigFile file) {
                retryStartRank = file.Bind("Options", "Retry Start Rank", 6, "The rank you get at the start of a retried wave\n0: Destructive\n1: Chaotic\n2: Brutal\n3: Anarchic\n4: Supreme\n5: SSadistic\n6: SSShitstorm\n7: ULTRAKILL");
                retryStartupTime = file.Bind("Options", "Retry Startup Time", 1f, "How long you wait before the retried wave reloads");
            }
        }

        static class Legacy {
            public static FieldInfo FinalCyberRank_countTime_field = AccessTools.Field(typeof(FinalCyberRank), "countTime");
            public static FieldInfo FinalCyberRank_countWaves_field = AccessTools.Field(typeof(FinalCyberRank), "countWaves");
            public static FieldInfo FinalCyberRank_countKills_field = AccessTools.Field(typeof(FinalCyberRank), "countKills");
            public static FieldInfo FinalCyberRank_countStyle_field = AccessTools.Field(typeof(FinalCyberRank), "countStyle");
            public static FieldInfo FinalCyberRank_friendContainer_field = AccessTools.Field(typeof(FinalCyberRank), "friendContainer");
            public static FieldInfo FinalCyberRank_globalContainer_field = AccessTools.Field(typeof(FinalCyberRank), "globalContainer");
            public static FieldInfo FinalCyberRank_complete_field = AccessTools.Field(typeof(FinalCyberRank), "complete");
            public static FieldInfo FinalCyberRank_i_field = AccessTools.Field(typeof(FinalCyberRank), "i");
            public static FieldInfo FinalCyberRank_gameOver_field = AccessTools.Field(typeof(FinalCyberRank), "gameOver");
            public static FieldInfo FinalCyberRank_wasPaused_field = AccessTools.Field(typeof(FinalCyberRank), "wasPaused");
            public static MethodInfo FinalCyberRank_GameOver_method = AccessTools.Method(typeof(FinalCyberRank), "GameOver");
            public static MethodInfo FinalCyberRank_Appear_method = AccessTools.Method(typeof(FinalCyberRank), "Appear");

            public static FieldInfo EndlessGrid_anw_field = AccessTools.Field(typeof(EndlessGrid), "anw");
            public static FieldInfo EndlessGrid_patterns_field = AccessTools.Field(typeof(EndlessGrid), "patterns");
            public static FieldInfo EndlessGrid_customPatterns_field = AccessTools.Field(typeof(EndlessGrid), "customPatterns");
            public static FieldInfo EndlessGrid_spawnedEnemies_field = AccessTools.Field(typeof(EndlessGrid), "spawnedEnemies");
            public static FieldInfo EndlessGrid_specialAntiBuffer_field = AccessTools.Field(typeof(EndlessGrid), "specialAntiBuffer");
            public static FieldInfo EndlessGrid_uncommonAntiBuffer_field = AccessTools.Field(typeof(EndlessGrid), "uncommonAntiBuffer");
            public static FieldInfo EndlessGrid_massAntiBuffer_field = AccessTools.Field(typeof(EndlessGrid), "massAntiBuffer");
            public static FieldInfo EndlessGrid_currentPatternNum_field = AccessTools.Field(typeof(EndlessGrid), "currentPatternNum");
            public static FieldInfo EndlessGrid_points_field = AccessTools.Field(typeof(EndlessGrid), "points");
            public static FieldInfo EndlessGrid_maxPoints_field = AccessTools.Field(typeof(EndlessGrid), "maxPoints");
            public static MethodInfo EndlessGrid_NextWave_method = AccessTools.Method(typeof(EndlessGrid), "NextWave");
            public static FieldInfo EndlessGrid_incompletePrefabs_field = AccessTools.Field(typeof(EndlessGrid), "incompletePrefabs");
            public static FieldInfo EndlessGrid_incompleteBlocks_field = AccessTools.Field(typeof(EndlessGrid), "incompleteBlocks");
            public static FieldInfo EndlessGrid_meleePositions_field = AccessTools.Field(typeof(EndlessGrid), "meleePositions");
            public static FieldInfo EndlessGrid_usedMeleePositions_field = AccessTools.Field(typeof(EndlessGrid), "usedMeleePositions");
            public static FieldInfo EndlessGrid_projectilePositions_field = AccessTools.Field(typeof(EndlessGrid), "projectilePositions");
            public static FieldInfo EndlessGrid_usedProjectilePositions_field = AccessTools.Field(typeof(EndlessGrid), "usedProjectilePositions");
            public static FieldInfo EndlessGrid_hideousMasses_field = AccessTools.Field(typeof(EndlessGrid), "hideousMasses");

            public static PropertyInfo StyleHUD_rankIndex_property = AccessTools.Property(typeof(StyleHUD), "rankIndex");
            public static FieldInfo StyleHUD_currentMeter_field = AccessTools.Field(typeof(StyleHUD), "currentMeter");
            public static MethodInfo StyleHUD_AscendRank_method = AccessTools.Method(typeof(StyleHUD), "AscendRank");

            public static FieldInfo OptionsManager_pauseMenu_field = AccessTools.Field(typeof(OptionsManager), "pauseMenu");

            public static FieldInfo ShotgunHammer_launchPlayer_field = AccessTools.Field(typeof(ShotgunHammer), "launchPlayer");
        }

        class RNG {
            static System.Random rng = new System.Random();

            public static void SetSeed(int seed) {
                rng = new System.Random(seed);
            }

            public static int RangeInt(int min, int max) {
                return rng.Next(min, max);
            }

            public static float RangeFloat(float min, float max) {
                return (float)(rng.NextDouble() * (max - min) + min);
            }
        }

        class DialogManager {
            public static GameObject pauseMenu;
            static GameObject quitDialog;
            public static GameObject gameOverDialog;
            public static GameObject retryDialog;

            public static void Initialize()
            {
                if (EndlessGrid.Instance == null) return;

                pauseMenu = (GameObject)Legacy.OptionsManager_pauseMenu_field.GetValue(OptionsManager.Instance);
                if (pauseMenu == null) return;

                Transform canvas = pauseMenu.transform.parent;
                GameObject pauseMenuDialogs = null;
                for (int i = 0; i < canvas.childCount; ++i) {
                    GameObject child = canvas.GetChild(i).gameObject;
                    if (child.name == "PauseMenuDialogs") pauseMenuDialogs = child;
                }
                if (pauseMenuDialogs == null) return;

                quitDialog = pauseMenuDialogs.transform.Find("Quit Confirm").gameObject;

                retryDialog = CloneDialog("Retry Confirm", "<color=green>RETRY</color>", "CANCEL", "Are you sure you want to\n<color=green>RETRY</color> this wave?",
                    RetryWave,
                    () => { });

                gameOverDialog = CloneDialog("Game Over Confirm", "<color=green>RETRY</color>", "NO", "Do you want to\n<color=green>RETRY</color> this wave?",
                    () => {
                        RetryWave();
                        SetMouseVisibility(false);
                    },
                    () =>
                    {
                        hasRefusedRetryOnDeath = true;
                        FinalCyberRank finalCyberRank = FindObjectOfType<FinalCyberRank>();
                        finalCyberRank.StopAllCoroutines();
                        Legacy.FinalCyberRank_Appear_method.Invoke(finalCyberRank, null);
                    });
            }

            public static void ShowDialog(GameObject dialog)
            {
                SetMouseVisibility(true);
                dialog.GetComponent<BasicConfirmationDialog>().ShowDialog();
                //pauseMenuDialogs.transform.Find("Blocker").gameObject.SetActive(true);
                //dialog.SetActive(true);
            }

            static void SetMouseVisibility(bool show)
            {
                Cursor.visible = show;
                Cursor.lockState = show ? CursorLockMode.None : CursorLockMode.Locked;
            }

            public static GameObject CloneDialog(string name, string confirmText, string cancelText, string text, UnityEngine.Events.UnityAction confirmEvent, UnityEngine.Events.UnityAction cancelEvent)
            {
                GameObject dialog = Instantiate(quitDialog, quitDialog.transform.parent);
                dialog.name = name;
                Transform panel = dialog.transform.Find("Panel");

                GameObject cancel = panel.Find("Cancel").gameObject;
                Button cancelButton = cancel.GetComponent<Button>();
                cancelButton.onClick.AddListener(cancelEvent);
                TextMeshProUGUI cancelButtonText = cancelButton.transform.Find("Text").GetComponent<TextMeshProUGUI>();
                cancelButtonText.text = cancelText;

                GameObject confirm = panel.Find("Confirm").gameObject;
                Button confirmButton = confirm.GetComponent<Button>();
                confirmButton.onClick = new Button.ButtonClickedEvent();
                confirmButton.onClick.AddListener(() =>
                {
                    dialog.transform.parent.Find("Blocker").gameObject.SetActive(false);
                    OptionsManager.Instance.UnPause();
                    dialog.SetActive(false);
                    SetMouseVisibility(false);
                });
                confirmButton.onClick.AddListener(confirmEvent);
                TextMeshProUGUI confirmButtonText = confirmButton.transform.Find("Text").GetComponent<TextMeshProUGUI>();
                confirmButtonText.text = confirmText;

                TextMeshProUGUI retryDialogText = panel.Find("Text (2)").gameObject.GetComponent<TextMeshProUGUI>();
                retryDialogText.text = text;

                panel.Find("Text (1)").gameObject.GetComponent<TextMeshProUGUI>().gameObject.SetActive(false);

                return dialog;
            }
        }

        static void TranspileRandom(IEnumerable<CodeInstruction> instructions) {
            MethodInfo Random_Range_int = AccessTools.Method(typeof(UnityEngine.Random), "Range", new System.Type[] { typeof(int), typeof(int) });
            MethodInfo Random_Range_float = AccessTools.Method(typeof(UnityEngine.Random), "Range", new System.Type[] { typeof(float), typeof(float) });

            MethodInfo Custom_Range_int = AccessTools.Method(typeof(RNG), "RangeInt", new System.Type[] { typeof(int), typeof(int) });
            MethodInfo Custom_Range_float = AccessTools.Method(typeof(RNG), "RangeFloat", new System.Type[] { typeof(float), typeof(float) });

            foreach (CodeInstruction instruction in instructions) {
                if (instruction.Calls(Random_Range_int)) {
                    instruction.operand = Custom_Range_int;
                }
                if (instruction.Calls(Random_Range_float)) {
                    instruction.operand = Custom_Range_float;
                }
            }
        }

        static void RetryWave() {
            CheatsController.Instance.ActivateCheats();
            ResetGame();
            isRetrying = true;
            Legacy.EndlessGrid_NextWave_method.Invoke(EndlessGrid.Instance, null);
        }

        static void ResetGame() {
            hasRefusedRetryOnDeath = false;

            GameObject currentWeapon = GunControl.Instance.currentWeapon;
            if (currentWeapon != null) { ShotgunHammer jackHammer = currentWeapon.GetComponent<ShotgunHammer>();
                if (jackHammer != null) {
                    Legacy.ShotgunHammer_launchPlayer_field.SetValue(jackHammer, false);
                }
            }

            // cybergrind
            Legacy.EndlessGrid_incompleteBlocks_field.SetValue(EndlessGrid.Instance, 999);
            ActivateNextWave anw = (ActivateNextWave)Legacy.EndlessGrid_anw_field.GetValue(EndlessGrid.Instance);
            anw.deadEnemies = 0;
            Legacy.EndlessGrid_anw_field.SetValue(EndlessGrid.Instance, anw);
            EndlessGrid.Instance.CancelInvoke();

            // player
            NewMovement.Instance.enabled = true;
            if (NewMovement.Instance.ridingRocket != null)
            {
                Destroy(NewMovement.Instance.ridingRocket.gameObject);
                NewMovement.Instance.ridingRocket = null;
            }
            NewMovement.Instance.StopSlide();
            NewMovement.Instance.Respawn();
            NewMovement.Instance.rb.position = new Vector3(0.01f, 100f, 62.5f);
            NewMovement.Instance.rb.velocity = Vector3.zero;

            // style
            StyleHUD.Instance.ResetAllFreshness();
            Legacy.StyleHUD_rankIndex_property.SetValue(StyleHUD.Instance, ConfigManager.retryStartRank.Value);
            Legacy.StyleHUD_currentMeter_field.SetValue(StyleHUD.Instance, StyleHUD.Instance.currentRank.maxMeter - 0.01f);
            Legacy.StyleHUD_AscendRank_method.Invoke(StyleHUD.Instance, null);

            // camera
            CameraController.Instance.activated = true;
            CameraController.Instance.StopShake();
            CameraController.Instance.rotationX = 0f;
            CameraController.Instance.rotationY = 0f;

            // stats
            StatsManager.Instance.StartTimer();
            StatsManager.Instance.UnhideShit();

            // ui
            FinalCyberRank finalCyberRank = FindObjectOfType<FinalCyberRank>();
            Legacy.FinalCyberRank_gameOver_field.SetValue(finalCyberRank, false);
            Legacy.FinalCyberRank_i_field.SetValue(finalCyberRank, 0);
            ((GameObject)Legacy.FinalCyberRank_friendContainer_field.GetValue(finalCyberRank))
                .transform.parent.gameObject.transform.parent.gameObject.transform.parent.gameObject.SetActive(false);
            ((GameObject)Legacy.FinalCyberRank_globalContainer_field.GetValue(finalCyberRank)).transform.parent.gameObject.SetActive(false);
            foreach (GameObject gameobject in finalCyberRank.toAppear) gameobject.SetActive(false);
            Legacy.FinalCyberRank_complete_field.SetValue(finalCyberRank, false);
            Legacy.FinalCyberRank_countTime_field.SetValue(finalCyberRank, false);
            Legacy.FinalCyberRank_countWaves_field.SetValue(finalCyberRank, false);
            Legacy.FinalCyberRank_countKills_field.SetValue(finalCyberRank, false);
            Legacy.FinalCyberRank_countStyle_field.SetValue(finalCyberRank, false);
            finalCyberRank.StopAllCoroutines();

            // time
            TimeController.Instance.controlTimeScale = true;

            // map
            DestroyAll<Projectile>();
            DestroyAll<ContinuousBeam>();
            DestroyAll<GroundWave>();
            DestroyAll<Magnet>();
            DestroyAll<VirtueInsignia>();
            DestroyAll<Pincer>();
            List<GameObject> enemies = (List<GameObject>)Legacy.EndlessGrid_spawnedEnemies_field.GetValue(EndlessGrid.Instance);
            for (int i = enemies.Count - 1; i >= 0; i--)
                if (enemies[i] != null) Destroy(enemies[i]);
            enemies.Clear();

            // audio
            Legacy.FinalCyberRank_wasPaused_field.SetValue(finalCyberRank, false);
            AudioMixerController.Instance.allSound.SetFloat("allVolume", AudioMixerController.Instance.CalculateVolume(AudioMixerController.Instance.sfxVolume));
            AudioMixerController.Instance.musicSound.SetFloat("allVolume", AudioMixerController.Instance.CalculateVolume(AudioMixerController.Instance.musicVolume));

            GameObject[] rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (GameObject gameobject in rootObjects)
                if (gameobject.name == "EndMusic") gameobject.SetActive(false);
        }

        class EndlessGridState {
            int seed;
            int points;
            int maxPoints;
            int specialAntiBuffer;
            int massAntiBuffer;
            float uncommonAntiBuffer;
            int currentWave;
            int currentPatternNum;
            ArenaPattern[] patterns;
            ArenaPattern[] customPatterns;
            int incompleteBlocks;
            int incompletePrefabs;
            List<Vector2> meleePositions;
            int usedMeleePositions;
            List<Vector2> projectilePositions;
            int usedProjectilePositions;
            int hideousMasses;

            public void Save(EndlessGrid grid) {
                Logger.LogInfo("Saved EndlessGrid state");
                seed = UnityEngine.Random.Range(0, int.MaxValue);
                RNG.SetSeed(seed);
                currentWave = grid.currentWave;
                points = (int)Legacy.EndlessGrid_points_field.GetValue(grid);
                maxPoints = (int)Legacy.EndlessGrid_maxPoints_field.GetValue(grid);
                specialAntiBuffer = (int)Legacy.EndlessGrid_specialAntiBuffer_field.GetValue(grid);
                massAntiBuffer = (int)Legacy.EndlessGrid_massAntiBuffer_field.GetValue(grid);
                uncommonAntiBuffer = (float)Legacy.EndlessGrid_uncommonAntiBuffer_field.GetValue(grid);
                currentPatternNum = (int)Legacy.EndlessGrid_currentPatternNum_field.GetValue(grid);
                patterns = (ArenaPattern[])((ArenaPattern[])Legacy.EndlessGrid_patterns_field.GetValue(grid)).Clone();
                customPatterns = (ArenaPattern[])((ArenaPattern[])Legacy.EndlessGrid_customPatterns_field.GetValue(grid)).Clone();

                incompleteBlocks = (int)Legacy.EndlessGrid_incompleteBlocks_field.GetValue(grid);
                usedMeleePositions = (int)Legacy.EndlessGrid_usedMeleePositions_field.GetValue(grid);
                usedProjectilePositions = (int)Legacy.EndlessGrid_usedProjectilePositions_field.GetValue(grid);
                incompletePrefabs = (int)Legacy.EndlessGrid_incompletePrefabs_field.GetValue(grid);
                hideousMasses = (int)Legacy.EndlessGrid_hideousMasses_field.GetValue(grid);
                meleePositions = new List<Vector2>((List<Vector2>)Legacy.EndlessGrid_meleePositions_field.GetValue(grid));
                projectilePositions = new List<Vector2>((List<Vector2>)Legacy.EndlessGrid_projectilePositions_field.GetValue(grid));
            }

            public void Load(EndlessGrid grid) {
                Logger.LogInfo("Loaded EndlessGrid state");
                RNG.SetSeed(seed);
                grid.currentWave = currentWave;
                Legacy.EndlessGrid_points_field.SetValue(grid, points);
                Legacy.EndlessGrid_maxPoints_field.SetValue(grid, maxPoints);
                Legacy.EndlessGrid_specialAntiBuffer_field.SetValue(grid, specialAntiBuffer);
                Legacy.EndlessGrid_massAntiBuffer_field.SetValue(grid, massAntiBuffer);
                Legacy.EndlessGrid_uncommonAntiBuffer_field.SetValue(grid, uncommonAntiBuffer);
                Legacy.EndlessGrid_currentPatternNum_field.SetValue(grid, currentPatternNum);
                Legacy.EndlessGrid_patterns_field.SetValue(grid, patterns.Clone());
                Legacy.EndlessGrid_customPatterns_field.SetValue(grid, customPatterns.Clone());

                Legacy.EndlessGrid_incompleteBlocks_field.SetValue(grid, incompleteBlocks);
                Legacy.EndlessGrid_usedMeleePositions_field.SetValue(grid, usedMeleePositions);
                Legacy.EndlessGrid_usedProjectilePositions_field.SetValue(grid, usedProjectilePositions);
                Legacy.EndlessGrid_incompletePrefabs_field.SetValue(grid, incompletePrefabs);
                Legacy.EndlessGrid_hideousMasses_field.SetValue(grid, hideousMasses);
                Legacy.EndlessGrid_meleePositions_field.SetValue(grid, new List<Vector2>(meleePositions));
                Legacy.EndlessGrid_projectilePositions_field.SetValue(grid, new List<Vector2>(projectilePositions));
            }
        }

        static void DestroyAll<T>() where T : Component {
            T[] objects = FindObjectsOfType<T>();
            if (objects.Length != 0)
                foreach (T obj in objects)
                    if (obj != null)
                        Destroy(obj.gameObject);
        }

        static IEnumerator WaitAndCall(float time, Action work)
        {
            yield return new WaitForSecondsRealtime(time);
            work();
        }

        [HarmonyPatch(typeof(EndlessGrid))]
        class EndlessGridPatch {
            [HarmonyPrefix]
            [HarmonyPatch("NextWave")]
            static bool NextWavePrefix() {
                if (!hasCbStarted) {
                    Logger.LogInfo($"Cybergrind started");
                    hasCbStarted = true;
                }

                if (!isRetrying) {
                    endlessGridState.Save(EndlessGrid.Instance);
                    return true;
                }

                if (isStartup) {
                    isStartup = false;
                    isRetrying = false;
                    return true;
                };

                EndlessGrid.Instance.StopAllCoroutines();
                EndlessGrid.Instance.StartCoroutine(WaitAndCall(ConfigManager.retryStartupTime.Value , () => {
                    endlessGridState.Load(EndlessGrid.Instance);
                    isStartup = true;
                    Legacy.EndlessGrid_NextWave_method.Invoke(EndlessGrid.Instance, null);
                }));
                return false;
            }

            [HarmonyTranspiler]
            [HarmonyPatch("GetEnemies")]
            static IEnumerable<CodeInstruction> GetEnemiesTranspiler(IEnumerable<CodeInstruction> instructions) {
                TranspileRandom(instructions);
                return instructions;
            }

            [HarmonyTranspiler]
            [HarmonyPatch("GetNextEnemy")]
            static IEnumerable<CodeInstruction> GetNextEnemyTranspiler(IEnumerable<CodeInstruction> instructions) {
                TranspileRandom(instructions);
                return instructions;
            }

            [HarmonyTranspiler]
            [HarmonyPatch("ShuffleDecks")]
            static IEnumerable<CodeInstruction> ShuffleDecksTranspiler(IEnumerable<CodeInstruction> instructions) {
                TranspileRandom(instructions);
                return instructions;
            }

            [HarmonyTranspiler]
            [HarmonyPatch("SpawnUncommons")]
            static IEnumerable<CodeInstruction> SpawnUncommonsTranspiler(IEnumerable<CodeInstruction> instructions) {
                TranspileRandom(instructions);
                return instructions;
            }
        }

        [HarmonyPatch(typeof(FinalCyberRank))]
        class FinalCyberRankPatch {
            [HarmonyPrefix]
            [HarmonyPatch("GameOver")]
            static bool GameOverPrefix(FinalCyberRank __instance) {
                if (!hasRefusedRetryOnDeath && hasCbStarted) {
                    __instance.StopAllCoroutines();
                    __instance.StartCoroutine(WaitAndCall(1f, () => {
                        DialogManager.ShowDialog(DialogManager.gameOverDialog);
                    }));
                }
                return true;
            }

            [HarmonyPrefix]
            [HarmonyPatch("Appear")]
            static bool AppearPrefix(FinalCyberRank __instance) {
                if (hasRefusedRetryOnDeath || !hasCbStarted) {
                    GameObject[] rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();
                    foreach (GameObject gameobject in rootObjects)
                        if (gameobject.name == "EndMusic") gameobject.SetActive(true);
                    return true;
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(OptionsManager))]
        class OptionsManagerPatch {
            static GameObject buttonObject;

            [HarmonyPostfix]
            [HarmonyPatch("Start")]
            static void StartPostfix() {
                DialogManager.Initialize();

                if (DialogManager.pauseMenu == null) return;

                Button templateButton = DialogManager.pauseMenu.GetComponentInChildren<Button>(true);
                buttonObject = Instantiate(templateButton.gameObject, templateButton.transform.parent);
                buttonObject.name = "CGRetryWave Button";
                Button buttonComponent = buttonObject.GetComponent<Button>();
                buttonComponent.onClick = new Button.ButtonClickedEvent();
                buttonComponent.onClick.AddListener(() =>
                {
                    FinalCyberRank finalCyberRank = FindObjectOfType<FinalCyberRank>();
                    bool gameOver = (bool)Legacy.FinalCyberRank_gameOver_field.GetValue(finalCyberRank);
                    if (CheatsController.Instance.cheatsEnabled || gameOver) {
                        OptionsManager.Instance.UnPause();
                        RetryWave();
                    }
                    else {
                        DialogManager.ShowDialog(DialogManager.retryDialog);
                    }
                });
                RectTransform rect = buttonObject.GetComponent<RectTransform>();
                rect.anchoredPosition += new Vector2(0f, -60f);
            }

            [HarmonyPostfix]
            [HarmonyPatch("Pause")]
            static void PausePostfix() {
                if (EndlessGrid.Instance == null) return;

                int incompleteBlocks = (int)Legacy.EndlessGrid_incompleteBlocks_field.GetValue(EndlessGrid.Instance);
                int incompletePrefabs = (int)Legacy.EndlessGrid_incompletePrefabs_field.GetValue(EndlessGrid.Instance);

                if (buttonObject) {
                    buttonObject.SetActive(hasCbStarted);
                    TextMeshProUGUI textComponent = buttonObject.GetComponentInChildren<TextMeshProUGUI>(true);
                    Button buttonComponent = buttonObject.GetComponent<Button>();
                    if (incompleteBlocks == 0 && incompletePrefabs == 0) {
                        textComponent.text = "<color=green>RETRY WAVE</color>";
                        buttonComponent.enabled = true;
                    }
                    else {
                        textComponent.text = "<color=red>LOADING WAVE</color>";
                        buttonComponent.enabled = false;
                    }
                }

                GameObject pauseMenu = (GameObject)Legacy.OptionsManager_pauseMenu_field.GetValue(OptionsManager.Instance);
                if (pauseMenu == null) return;

                Button[] buttons = pauseMenu.GetComponentsInChildren<Button>();
                foreach (Button button in buttons) {
                    TextMeshProUGUI textComponent = button.GetComponentInChildren<TextMeshProUGUI>(true);
                    if (textComponent != null && textComponent.text.ToUpper() == "CHECKPOINT") {
                        button.gameObject.SetActive(!hasCbStarted);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(NewMovement))]
        class NewMovementPatch {
            [HarmonyPrefix]
            [HarmonyPatch("Start")]
            static void StartPrefix() {
                hasCbStarted = false;
                hasRefusedRetryOnDeath = false;
            }
        }

        [HarmonyPatch(typeof(BasicConfirmationDialog))]
        class BasicConfirmationDialogPatch {
            [HarmonyPrefix]
            [HarmonyPatch("Update")]
            static bool UpdatePrefix(BasicConfirmationDialog __instance) {
                if (__instance.gameObject.name == "Game Over Confirm") return false;
                return true;
            }
        }
    }
}