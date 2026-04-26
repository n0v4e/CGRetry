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
                pauseMenu = OptionsManager.Instance.pauseMenu;
                if (OptionsManager.Instance.pauseMenu == null) return;

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
                        finalCyberRank.Appear();
                    });
            }

            public static void ShowDialog(GameObject dialog) {
                SetMouseVisibility(true);
                dialog.GetComponent<BasicConfirmationDialog>().ShowDialog();
            }

            static void SetMouseVisibility(bool show) {
                Cursor.visible = show;
                Cursor.lockState = show ? CursorLockMode.None : CursorLockMode.Locked;
            }

            public static GameObject CloneDialog(string name, string confirmText, string cancelText, string text, UnityEngine.Events.UnityAction confirmEvent, UnityEngine.Events.UnityAction cancelEvent) {
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
            EndlessGrid.Instance.NextWave();
        }

        static void ResetGame() {
            hasRefusedRetryOnDeath = false;

            GameObject currentWeapon = GunControl.Instance.currentWeapon;
            if (currentWeapon != null) {
                ShotgunHammer jackHammer = currentWeapon.GetComponent<ShotgunHammer>();
                if (jackHammer != null) jackHammer.launchPlayer = false;
            }

            // cybergrind
            EndlessGrid.Instance.incompleteBlocks = 999;
            EndlessGrid.Instance.anw.deadEnemies = 0;
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
            StyleHUD.Instance.rankIndex = ConfigManager.retryStartRank.Value;
            StyleHUD.Instance.currentMeter = StyleHUD.Instance.currentRank.maxMeter - 0.01f;
            StyleHUD.Instance.AscendRank();

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
            finalCyberRank.gameOver = false;
            finalCyberRank.i = 0;
            finalCyberRank.friendContainer.transform.parent.gameObject.transform.parent.gameObject.transform.parent.gameObject.SetActive(false);
            finalCyberRank.globalContainer.transform.parent.gameObject.SetActive(false);
            foreach (GameObject gameobject in finalCyberRank.toAppear) gameobject.SetActive(false);
            finalCyberRank.complete = false;
            finalCyberRank.countTime = false;
            finalCyberRank.countWaves = false;
            finalCyberRank.countKills = false;
            finalCyberRank.countStyle = false;
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
            // destroy shockwaves
            // destroy goopclouds

            List<GameObject> enemies = EndlessGrid.Instance.spawnedEnemies;
            for (int i = enemies.Count - 1; i >= 0; i--)
                if (enemies[i] != null) Destroy(enemies[i]);
            enemies.Clear();

            // audio
            finalCyberRank.wasPaused = false;
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
                points = EndlessGrid.Instance.points;
                maxPoints = EndlessGrid.Instance.maxPoints;
                specialAntiBuffer = EndlessGrid.Instance.specialAntiBuffer;
                massAntiBuffer = EndlessGrid.Instance.massAntiBuffer;
                uncommonAntiBuffer = EndlessGrid.Instance.uncommonAntiBuffer;
                currentPatternNum = EndlessGrid.Instance.currentPatternNum;
                patterns = (ArenaPattern[])EndlessGrid.Instance.patterns.Clone();
                customPatterns = (ArenaPattern[])EndlessGrid.Instance.customPatterns.Clone();
                incompleteBlocks = EndlessGrid.Instance.incompleteBlocks;
                usedMeleePositions = EndlessGrid.Instance.usedMeleePositions;
                usedProjectilePositions = EndlessGrid.Instance.usedProjectilePositions;
                incompletePrefabs = EndlessGrid.Instance.incompletePrefabs;
                hideousMasses = EndlessGrid.Instance.hideousMasses;
                meleePositions = new List<Vector2>(EndlessGrid.Instance.meleePositions);
                projectilePositions = new List<Vector2>(EndlessGrid.Instance.projectilePositions);
            }

            public void Load(EndlessGrid grid) {
                Logger.LogInfo("Loaded EndlessGrid state");
                RNG.SetSeed(seed);
                grid.currentWave = currentWave;
                EndlessGrid.Instance.points = points;
                EndlessGrid.Instance.maxPoints= maxPoints;
                EndlessGrid.Instance.specialAntiBuffer =  specialAntiBuffer;
                EndlessGrid.Instance.massAntiBuffer = massAntiBuffer;
                EndlessGrid.Instance.uncommonAntiBuffer = uncommonAntiBuffer;
                EndlessGrid.Instance.currentPatternNum = currentPatternNum;
                EndlessGrid.Instance.patterns = (ArenaPattern[])patterns.Clone();
                EndlessGrid.Instance.customPatterns = (ArenaPattern[])customPatterns.Clone();
                EndlessGrid.Instance.incompleteBlocks = incompleteBlocks;
                EndlessGrid.Instance.usedMeleePositions = usedMeleePositions;
                EndlessGrid.Instance.usedProjectilePositions = usedProjectilePositions;
                EndlessGrid.Instance.incompletePrefabs = incompletePrefabs;
                EndlessGrid.Instance.hideousMasses = hideousMasses;
                EndlessGrid.Instance.meleePositions = new List<Vector2>(meleePositions);
                EndlessGrid.Instance.projectilePositions = new List<Vector2>(projectilePositions);
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
                    EndlessGrid.Instance.NextWave();
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
                    if (CheatsController.Instance.cheatsEnabled || finalCyberRank.gameOver) {
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

                if (buttonObject) {
                    buttonObject.SetActive(hasCbStarted);
                    TextMeshProUGUI textComponent = buttonObject.GetComponentInChildren<TextMeshProUGUI>(true);
                    Button buttonComponent = buttonObject.GetComponent<Button>();
                    if (EndlessGrid.Instance.incompleteBlocks == 0 && EndlessGrid.Instance.incompletePrefabs == 0) {
                        textComponent.text = "<color=green>RETRY WAVE</color>";
                        buttonComponent.enabled = true;
                    }
                    else {
                        textComponent.text = "<color=red>LOADING WAVE</color>";
                        buttonComponent.enabled = false;
                    }
                }

                if (OptionsManager.Instance.pauseMenu == null) return;
                Button[] buttons = OptionsManager.Instance.pauseMenu.GetComponentsInChildren<Button>();
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