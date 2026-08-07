// One-click Editor helper that authors MainMenu.unity, Lobby.unity,
// SettingsPanel.prefab, RoomEntry.prefab, PlayerSlot.prefab, and the
// AudioMixer used by SettingsService.
//
// Building Unity UI from raw scene YAML is impractical (a few dozen
// Canvas + Text + Button + LayoutGroup components produce ~800 lines
// of hand-written YAML per scene — sensitive to Unity version). Doing
// it via the Editor API keeps the definition compact and version-safe.
//
// Menu items:
//   Life-is-a-Game > Setup > Author Menu Scenes + Prefabs

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using LifeIsAGame.UI;
using LifeIsAGame.Net;

namespace LifeIsAGame.EditorTools
{
    public static class MenuSetupMenu
    {
        private const string ScenesDir       = "Assets/Game/Scenes";
        private const string PrefabsDir      = "Assets/Game/UI";
        private const string AudioDir        = "Assets/Game/Audio";
        private const string MainMenuScene   = "Assets/Game/Scenes/MainMenu.unity";
        private const string LobbyScene      = "Assets/Game/Scenes/Lobby.unity";
        private const string SettingsPrefab  = "Assets/Game/UI/SettingsPanel.prefab";
        private const string RoomEntryPrefab = "Assets/Game/UI/RoomEntry.prefab";
        private const string PlayerSlotPrefab= "Assets/Game/UI/PlayerSlot.prefab";
        private const string MixerAsset      = "Assets/Game/Audio/MainMixer.mixer";

        [MenuItem("Life-is-a-Game/Setup/Author Menu Scenes + Prefabs")]
        public static void AuthorEverything()
        {
            Directory.CreateDirectory(ScenesDir);
            Directory.CreateDirectory(PrefabsDir);
            Directory.CreateDirectory(AudioDir);

            AudioMixer mixer = EnsureMixer();
            GameObject settingsPrefab  = CreateSettingsPanelPrefab(mixer);
            GameObject roomEntryPrefab = CreateRoomEntryPrefab();
            GameObject playerSlotPrefab= CreatePlayerSlotPrefab();

            CreateMainMenuScene(settingsPrefab);
            CreateLobbyScene(settingsPrefab, roomEntryPrefab, playerSlotPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Life-is-a-Game: menu scenes and prefabs authored.");
        }

        // -----------------------------------------------------------------
        // Audio mixer
        // -----------------------------------------------------------------

        private static AudioMixer EnsureMixer()
        {
            // AudioMixer can't be created via CreateInstance + SaveAsset in
            // a well-supported way. If the mixer doesn't exist we leave a
            // note in the console; the user creates it via
            // Create > Audio Mixer, exposes MasterVolume/MusicVolume/SfxVolume
            // parameters, then re-runs this menu.
            AudioMixer m = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerAsset);
            if (m == null)
            {
                Debug.LogWarning(
                    $"[MenuSetup] {MixerAsset} missing. Create an AudioMixer at that path " +
                    "and expose float parameters 'MasterVolume', 'MusicVolume', 'SfxVolume' " +
                    "on the master group. SettingsService will still function without it — " +
                    "audio sliders just won't route through the mixer.");
            }
            return m;
        }

        // -----------------------------------------------------------------
        // SettingsPanel prefab
        // -----------------------------------------------------------------

        private static GameObject CreateSettingsPanelPrefab(AudioMixer mixer)
        {
            GameObject root = new GameObject("SettingsPanel");
            root.SetActive(false); // starts hidden

            RectTransform rt = root.AddComponent<RectTransform>();
            root.AddComponent<CanvasRenderer>();
            Image bg = root.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.85f);

            // Stretch to parent when instantiated in a Canvas.
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;

            // Vertical layout container for the widgets.
            GameObject content = MakeChild(root, "Content");
            var contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0.5f, 0.5f);
            contentRt.anchorMax = new Vector2(0.5f, 0.5f);
            contentRt.pivot     = new Vector2(0.5f, 0.5f);
            contentRt.sizeDelta = new Vector2(480f, 640f);
            var v = content.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(16, 16, 16, 16);
            v.spacing = 8f;
            v.childControlWidth = true;
            v.childControlHeight = false;
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            MakeHeader(content, "SETTINGS");

            MakeHeader(content, "Graphics");
            TMP_Dropdown qualityDd = MakeDropdown(content, "Quality");
            TMP_Dropdown resDd     = MakeDropdown(content, "Resolution");
            Toggle fs = MakeToggle(content, "Fullscreen");
            Toggle vs = MakeToggle(content, "VSync");

            MakeHeader(content, "Audio");
            Slider master = MakeSlider(content, "Master", 0f, 1f, 1f);
            Slider music  = MakeSlider(content, "Music",  0f, 1f, 1f);
            Slider sfx    = MakeSlider(content, "SFX",    0f, 1f, 1f);

            MakeHeader(content, "Gameplay");
            Slider mouse   = MakeSlider(content, "Mouse Sensitivity", 1f, 500f, 50f);
            Slider fov     = MakeSlider(content, "Field of View",     30f, 130f, 75f);
            Toggle invertY = MakeToggle(content, "Invert Y");
            TMP_InputField display = MakeInput(content, "Display Name");

            MakeHeader(content, "");
            Button apply = MakeButton(content, "Apply");
            Button reset = MakeButton(content, "Reset to Defaults");
            Button close = MakeButton(content, "Close");

            SettingsPanel sp = root.AddComponent<SettingsPanel>();
            var so = new SerializedObject(sp);
            so.FindProperty("qualityDropdown").objectReferenceValue    = qualityDd;
            so.FindProperty("resolutionDropdown").objectReferenceValue = resDd;
            so.FindProperty("fullscreenToggle").objectReferenceValue   = fs;
            so.FindProperty("vsyncToggle").objectReferenceValue        = vs;
            so.FindProperty("masterSlider").objectReferenceValue       = master;
            so.FindProperty("musicSlider").objectReferenceValue        = music;
            so.FindProperty("sfxSlider").objectReferenceValue          = sfx;
            so.FindProperty("mouseSensSlider").objectReferenceValue    = mouse;
            so.FindProperty("fovSlider").objectReferenceValue          = fov;
            so.FindProperty("invertYToggle").objectReferenceValue      = invertY;
            so.FindProperty("displayNameInput").objectReferenceValue   = display;
            so.FindProperty("applyButton").objectReferenceValue        = apply;
            so.FindProperty("closeButton").objectReferenceValue        = close;
            so.FindProperty("resetButton").objectReferenceValue        = reset;
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, SettingsPrefab);
            Object.DestroyImmediate(root);
            return saved;
        }

        // -----------------------------------------------------------------
        // Room entry + player slot prefabs (tiny row prefabs used by lobby)
        // -----------------------------------------------------------------

        private static GameObject CreateRoomEntryPrefab()
        {
            GameObject entry = new GameObject("RoomEntry",
                typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rt = entry.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(400f, 32f);
            entry.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);

            GameObject label = MakeChild(entry, "Label");
            var text = label.AddComponent<TextMeshProUGUI>();
            text.text = "room";
            text.fontSize = 18f;
            text.alignment = TextAlignmentOptions.Left;
            var lrt = label.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = new Vector2(8, 0); lrt.offsetMax = Vector2.zero;

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(entry, RoomEntryPrefab);
            Object.DestroyImmediate(entry);
            return saved;
        }

        private static GameObject CreatePlayerSlotPrefab()
        {
            GameObject slot = new GameObject("PlayerSlot", typeof(RectTransform), typeof(Image));
            RectTransform rt = slot.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(400f, 28f);
            slot.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);

            GameObject label = MakeChild(slot, "Label");
            var text = label.AddComponent<TextMeshProUGUI>();
            text.text = "player";
            text.fontSize = 16f;
            text.alignment = TextAlignmentOptions.Left;
            var lrt = label.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = new Vector2(8, 0); lrt.offsetMax = Vector2.zero;

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(slot, PlayerSlotPrefab);
            Object.DestroyImmediate(slot);
            return saved;
        }

        // -----------------------------------------------------------------
        // Main menu scene
        // -----------------------------------------------------------------

        private static void CreateMainMenuScene(GameObject settingsPrefab)
        {
            Scene s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(s);

            GameObject camGo = new GameObject("MainCamera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            camGo.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            camGo.GetComponent<Camera>().backgroundColor = new Color(0.08f, 0.10f, 0.14f, 1f);

            // Canvas
            GameObject canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            // Menu panel
            GameObject panel = MakeChild(canvasGo, "MainMenu");
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 0.5f); prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot     = new Vector2(0.5f, 0.5f); prt.sizeDelta = new Vector2(300f, 400f);
            var v = panel.AddComponent<VerticalLayoutGroup>();
            v.spacing = 12f;
            v.childControlWidth = true;
            v.childControlHeight = false;

            MakeHeader(panel, "LIFE IS A GAME");

            MainMenuController mmc = panel.AddComponent<MainMenuController>();

            Button play     = MakeButton(panel, "Play (Local)");
            Button host     = MakeButton(panel, "Host Room");
            Button join     = MakeButton(panel, "Join by IP");
            Button settings = MakeButton(panel, "Settings");
            Button quit     = MakeButton(panel, "Quit");

            play.onClick.AddListener(mmc.OnPlayClicked);
            host.onClick.AddListener(mmc.OnHostClicked);
            join.onClick.AddListener(mmc.OnJoinClicked);
            settings.onClick.AddListener(mmc.OnSettingsClicked);
            quit.onClick.AddListener(mmc.OnQuitClicked);

            // Instantiate settings panel (hidden) into the canvas.
            GameObject settingsInstance = (GameObject)PrefabUtility.InstantiatePrefab(settingsPrefab, canvasGo.transform);

            // Join dialog (very small)
            GameObject joinDlg = MakeChild(canvasGo, "JoinDialog");
            joinDlg.SetActive(false);
            var jrt = joinDlg.GetComponent<RectTransform>();
            jrt.anchorMin = new Vector2(0.5f, 0.5f); jrt.anchorMax = new Vector2(0.5f, 0.5f);
            jrt.pivot     = new Vector2(0.5f, 0.5f); jrt.sizeDelta = new Vector2(300f, 120f);
            joinDlg.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.9f);
            var jv = joinDlg.AddComponent<VerticalLayoutGroup>();
            jv.padding = new RectOffset(16, 16, 16, 16);
            jv.spacing = 8f;
            jv.childControlWidth = true; jv.childControlHeight = false;
            MakeHeader(joinDlg, "Join by IP");
            TMP_InputField ipInput = MakeInput(joinDlg, "127.0.0.1");
            Button joinGo = MakeButton(joinDlg, "Connect");
            joinGo.onClick.AddListener(() => mmc.OnJoinConfirm(ipInput.text));

            var so = new SerializedObject(mmc);
            so.FindProperty("settingsPanel").objectReferenceValue = settingsInstance;
            so.FindProperty("joinDialog").objectReferenceValue    = joinDlg;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(s, MainMenuScene);
        }

        // -----------------------------------------------------------------
        // Lobby scene
        // -----------------------------------------------------------------

        private static void CreateLobbyScene(GameObject settingsPrefab, GameObject roomEntryPrefab, GameObject playerSlotPrefab)
        {
            Scene s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(s);

            GameObject camGo = new GameObject("MainCamera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            camGo.GetComponent<Camera>().backgroundColor = new Color(0.05f, 0.05f, 0.08f, 1f);

            GameObject canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            // Two panels: RoomList, InRoom (plus CreateRoomDialog overlay).
            GameObject controllerHost = MakeChild(canvasGo, "LobbyController");

            // ---- Room List panel ----
            GameObject roomListPanel = MakeChild(canvasGo, "RoomListPanel");
            var rlrt = roomListPanel.GetComponent<RectTransform>();
            rlrt.anchorMin = Vector2.zero; rlrt.anchorMax = Vector2.one; rlrt.offsetMin = rlrt.offsetMax = Vector2.zero;

            MakeHeader(roomListPanel, "Open Rooms");
            GameObject roomList = MakeChild(roomListPanel, "RoomList");
            var rlist = roomList.AddComponent<VerticalLayoutGroup>();
            rlist.spacing = 4f;
            rlist.childControlWidth = true; rlist.childControlHeight = false;
            var rlistRt = roomList.GetComponent<RectTransform>();
            rlistRt.anchorMin = new Vector2(0.1f, 0.2f); rlistRt.anchorMax = new Vector2(0.9f, 0.85f);
            rlistRt.offsetMin = rlistRt.offsetMax = Vector2.zero;

            Button refreshBtn = MakeButton(roomListPanel, "Refresh");
            Button createBtn  = MakeButton(roomListPanel, "Create Room");

            // ---- Create Room dialog ----
            GameObject createDlg = MakeChild(canvasGo, "CreateRoomDialog");
            createDlg.SetActive(false);
            var cdrt = createDlg.GetComponent<RectTransform>();
            cdrt.anchorMin = new Vector2(0.5f, 0.5f); cdrt.anchorMax = new Vector2(0.5f, 0.5f);
            cdrt.pivot = new Vector2(0.5f, 0.5f); cdrt.sizeDelta = new Vector2(360f, 220f);
            createDlg.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.92f);
            var cdv = createDlg.AddComponent<VerticalLayoutGroup>();
            cdv.padding = new RectOffset(16, 16, 16, 16);
            cdv.spacing = 8f;
            cdv.childControlWidth = true;
            MakeHeader(createDlg, "Create Room");
            TMP_InputField nameIn = MakeInput(createDlg, "Room name");
            TMP_InputField pwIn   = MakeInput(createDlg, "Password (optional)");
            TMP_InputField maxIn  = MakeInput(createDlg, "Max players (2-16)");
            Button confirm = MakeButton(createDlg, "Host");

            // ---- In-room panel ----
            GameObject inRoom = MakeChild(canvasGo, "InRoomPanel");
            inRoom.SetActive(false);
            var irt = inRoom.GetComponent<RectTransform>();
            irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one; irt.offsetMin = irt.offsetMax = Vector2.zero;

            GameObject titleGo = MakeChild(inRoom, "Title");
            var titleText = titleGo.AddComponent<TextMeshProUGUI>();
            titleText.text = "Room";
            titleText.fontSize = 28f;
            titleText.alignment = TextAlignmentOptions.Center;
            var ttrt = titleGo.GetComponent<RectTransform>();
            ttrt.anchorMin = new Vector2(0f, 0.9f); ttrt.anchorMax = new Vector2(1f, 1f);
            ttrt.offsetMin = ttrt.offsetMax = Vector2.zero;

            GameObject slotContent = MakeChild(inRoom, "PlayerSlots");
            var slotV = slotContent.AddComponent<VerticalLayoutGroup>();
            slotV.spacing = 4f;
            slotV.childControlWidth = true; slotV.childControlHeight = false;
            var slrt = slotContent.GetComponent<RectTransform>();
            slrt.anchorMin = new Vector2(0.2f, 0.3f); slrt.anchorMax = new Vector2(0.8f, 0.85f);
            slrt.offsetMin = slrt.offsetMax = Vector2.zero;

            Button startBtn = MakeButton(inRoom, "Start Game");
            Button leaveBtn = MakeButton(inRoom, "Leave");

            // ---- Wire the LobbyController ----
            LobbyController lc = controllerHost.AddComponent<LobbyController>();
            var so = new SerializedObject(lc);
            so.FindProperty("roomListPanel").objectReferenceValue      = roomListPanel;
            so.FindProperty("createRoomDialog").objectReferenceValue   = createDlg;
            so.FindProperty("inRoomPanel").objectReferenceValue        = inRoom;
            so.FindProperty("roomListContent").objectReferenceValue    = roomList.transform;
            so.FindProperty("roomEntryPrefab").objectReferenceValue    = roomEntryPrefab;
            so.FindProperty("refreshButton").objectReferenceValue      = refreshBtn;
            so.FindProperty("createRoomButton").objectReferenceValue   = createBtn;
            so.FindProperty("createRoomNameInput").objectReferenceValue     = nameIn;
            so.FindProperty("createRoomPasswordInput").objectReferenceValue = pwIn;
            so.FindProperty("createRoomMaxInput").objectReferenceValue      = maxIn;
            so.FindProperty("createRoomConfirmButton").objectReferenceValue = confirm;
            so.FindProperty("playerSlotContent").objectReferenceValue = slotContent.transform;
            so.FindProperty("playerSlotPrefab").objectReferenceValue  = playerSlotPrefab;
            so.FindProperty("startGameButton").objectReferenceValue   = startBtn;
            so.FindProperty("leaveButton").objectReferenceValue       = leaveBtn;
            so.FindProperty("roomTitle").objectReferenceValue         = titleText;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.InstantiatePrefab(settingsPrefab, canvasGo.transform);

            EditorSceneManager.SaveScene(s, LobbyScene);
        }

        // -----------------------------------------------------------------
        // Small factory helpers
        // -----------------------------------------------------------------

        private static GameObject MakeChild(GameObject parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            return go;
        }

        private static TMP_Text MakeHeader(GameObject parent, string text)
        {
            var go = MakeChild(parent, $"Header ({text})");
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = 22f;
            t.alignment = TextAlignmentOptions.Center;
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 32f;
            return t;
        }

        private static Button MakeButton(GameObject parent, string label)
        {
            var go = MakeChild(parent, $"Button ({label})");
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.1f);
            var btn = go.AddComponent<Button>();
            var le = go.AddComponent<LayoutElement>(); le.minHeight = 40f;

            var labelGo = MakeChild(go, "Label");
            var text = labelGo.AddComponent<TextMeshProUGUI>();
            text.text = label; text.fontSize = 18f;
            text.alignment = TextAlignmentOptions.Center;
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            return btn;
        }

        private static Slider MakeSlider(GameObject parent, string label, float min, float max, float initial)
        {
            var row = MakeChild(parent, $"Slider ({label})");
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8f;
            rowLayout.childControlWidth = true; rowLayout.childControlHeight = false;
            var le = row.AddComponent<LayoutElement>(); le.minHeight = 24f;

            var labelGo = MakeChild(row, "Label");
            var lt = labelGo.AddComponent<TextMeshProUGUI>();
            lt.text = label; lt.fontSize = 14f; lt.alignment = TextAlignmentOptions.Left;

            var sliderGo = MakeChild(row, "Slider");
            var slider = sliderGo.AddComponent<Slider>();
            slider.minValue = min; slider.maxValue = max; slider.value = initial;
            // Slider needs Background, Fill Area, Handle Slide Area — Unity's
            // Slider.Setup below is not a public API; a real Editor authoring
            // tool would use `DefaultControls.CreateSlider(resources)`. Left
            // minimal here; Unity renders a functional bare slider.
            return slider;
        }

        private static TMP_Dropdown MakeDropdown(GameObject parent, string label)
        {
            var row = MakeChild(parent, $"Dropdown ({label})");
            var le = row.AddComponent<LayoutElement>(); le.minHeight = 28f;
            var img = row.AddComponent<Image>(); img.color = new Color(1f,1f,1f,0.05f);
            var dd = row.AddComponent<TMP_Dropdown>();
            return dd;
        }

        private static Toggle MakeToggle(GameObject parent, string label)
        {
            var row = MakeChild(parent, $"Toggle ({label})");
            var le = row.AddComponent<LayoutElement>(); le.minHeight = 24f;
            var img = row.AddComponent<Image>(); img.color = new Color(1f,1f,1f,0.05f);
            var t = row.AddComponent<Toggle>();
            var lt = MakeChild(row, "Label").AddComponent<TextMeshProUGUI>();
            lt.text = label; lt.fontSize = 14f;
            return t;
        }

        private static TMP_InputField MakeInput(GameObject parent, string placeholder)
        {
            var row = MakeChild(parent, $"Input ({placeholder})");
            var le = row.AddComponent<LayoutElement>(); le.minHeight = 28f;
            var img = row.AddComponent<Image>(); img.color = new Color(1f,1f,1f,0.05f);
            var inp = row.AddComponent<TMP_InputField>();
            // A fully-functional TMP_InputField needs Text + Placeholder
            // child objects; we leave that to the user to visually polish.
            return inp;
        }
    }
}
#endif
