using System;
using System.Collections.Generic;
using System.Linq;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Game.Utility.ModSupport.ModSettings;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Utility.AssetInjection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Mods.WorldTooltips.Scripts
{
    public class Modded_HUDTooltipWindow : Panel
    {
        #region Singleton

        public static Modded_HUDTooltipWindow Instance { get; private set; }

        #endregion Singleton

        #region Fields

        private static Dictionary<string, string> textDataBase;
        public static string TooltipBgTopLeftName = "tooltip_bg_0-0.png";
        public static string TooltipBgTopName = "tooltip_bg_1-0.png";
        public static string TooltipBgTopRightName = "tooltip_bg_2-0.png";
        public static string TooltipBgLeftName = "tooltip_bg_3-0.png";
        public static string TooltipBgFillName = "tooltip_bg_4-0.png";
        public static string TooltipBgRightName = "tooltip_bg_5-0.png";
        public static string TooltipBgBottomLeftName = "tooltip_bg_6-0.png";
        public static string TooltipBgBottomName = "tooltip_bg_7-0.png";
        public static string TooltipBgBottomRightName = "tooltip_bg_8-0.png";
        public static Texture2D TooltipBgTopLeft;
        public static Texture2D TooltipBgTop;
        public static Texture2D TooltipBgTopRight;
        public static Texture2D TooltipBgLeft;
        public static Texture2D TooltipBgFill;
        public static Texture2D TooltipBgRight;
        public static Texture2D TooltipBgBottomLeft;
        public static Texture2D TooltipBgBottom;
        public static Texture2D TooltipBgBottomRight;

        // Special cases
        public static bool PenwickPeepIsOn;

        #region Settings

        public static bool HideInteractTooltip { get; private set; }
        public static bool ShowHiddenDoorsTooltip { get; private set; }
        public static bool OnlyInInfoMode { get; private set; }
        public static bool NamesOfRestrainedFoes { get; private set; }
        public static bool CenterText { get; private set; }
        public static bool Textured { get; private set; }
        public static bool ShowLockLevel { get; private set; }
        public static bool ShowLockStatus { get; private set; }
        public static bool ShowLockOpenChance { get; private set; }
        public static int FontIndex { get; private set; }
        public static float FontScale { get; private set; }
        public static Color32 BgColor { get; private set; }
        public static Color32 TextColor { get; private set; }

        // Tooltip positioning offsets relative to crosshair
        public static int TooltipOffsetX { get; private set; } = 0;
        public static int TooltipOffsetY { get; private set; } = 0;

        #endregion Settings

        #region Mod API

        private const string REGISTER_CUSTOM_TOOLTIP = "RegisterCustomTooltip";
        private static Mod mod;

        #endregion Mod API

        #region Raycasting

        //Use the farthest distance
        private const float rayDistance = PlayerActivate.StaticNPCActivationDistance;

        private static Dictionary<float, List<Func<RaycastHit, string>>> customGetHoverText = new Dictionary<float, List<Func<RaycastHit, string>>>();

        private GameObject mainCamera;
        private int playerLayerMask;

        //Caching
        private Transform prevHit;
        private string prevText;

        private GameObject goDoor;
        private BoxCollider goDoorCollider;
        private StaticDoor prevDoor;
        private string prevDoorText;
        private float prevDistance;
        private Dictionary<int, DoorData> doorDataDict = new Dictionary<int, DoorData>();

        private Transform prevDoorCheckTransform;
        private Transform prevDoorOwner;
        private DaggerfallStaticDoors prevStaticDoors;

        #endregion Raycasting

        private PlayerEnterExit playerEnterExit;
        private PlayerGPS playerGPS;
        private PlayerActivate playerActivate;
        private HUDTooltip tooltip;

        #region Stolen methods/variables/properties

        private byte[] openHours;
        private byte[] closeHours;
        private static readonly int _scissorRect = Shader.PropertyToID("_ScissorRect");

        #endregion Stolen methods/variables/properties

        #endregion Fields

        [Invoke(StateManager.StateTypes.Start, 0)]
        public static void Init(InitParams initParams)
        {
            mod = initParams.Mod;
            mod.MessageReceiver = MessageReceiver;
            mod.LoadSettingsCallback = LoadSettings;
            StateManager.OnStartNewGame += OnGameStarted;
            StartGameBehaviour.OnStartGame += OnNewGameStarted;

            // "The Penwick Papers"
            var thePenwickPapersInstance = ModManager.Instance.GetModFromGUID("c7a201b3-3b41-4c44-a5c6-f86c77cca630");
            var penwickModEnabled = thePenwickPapersInstance != null && thePenwickPapersInstance.Enabled;
            if (penwickModEnabled)
            {
                PenwickPeepIsOn = thePenwickPapersInstance.GetSettings().GetBool("Features", "DirtyTricks-Peep");
            }

            LoadTextures();
            LoadTextData();

            mod.IsReady = true;
        }

        private void Start()
        {
            mod.LoadSettings();
        }

        private static void OnGameStarted(object sender, EventArgs e)
        {
            mod.LoadSettings();
        }

        private static void OnNewGameStarted(object sender, EventArgs e)
        {
            mod.LoadSettings();
        }

        [Invoke(StateManager.StateTypes.Game)]
        public static void InitAtGameState(InitParams initParams)
        {
            Debug.Log("****************************Init HUD tooltips");
            Instance = new Modded_HUDTooltipWindow();

            DaggerfallUI.Instance.DaggerfallHUD.ParentPanel.Components.Add(Instance);
        }

        #region Constructors

        public Modded_HUDTooltipWindow()
        {
            // Ray casting
            mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
            playerLayerMask = ~(1 << LayerMask.NameToLayer("Player"));

            // Reading
            playerEnterExit = GameManager.Instance.PlayerEnterExit;
            playerGPS = GameManager.Instance.PlayerGPS;
            playerActivate = GameManager.Instance.PlayerActivate;

            openHours = PlayerActivate.openHours;
            closeHours = PlayerActivate.closeHours;

            tooltip = new HUDTooltip();
            Components.Add(tooltip);

            PlayerGPS.OnMapPixelChanged += _ =>
            {
                Debug.Log("***************Clearing door data cache..");
                doorDataDict.Clear();
                // Also cleanup door GameObject when changing map pixels
                CleanupDoorGameObject();
            };
        }

        #endregion Constructors

        #region Lifecycle Methods

        /// <summary>
        /// Cleanup when component is destroyed to prevent lingering GameObjects
        /// </summary>
        private void OnDestroy()
        {
            CleanupDoorGameObject();
        }

        /// <summary>
        /// Cleanup when component is disabled to prevent interference
        /// </summary>
        private void OnDisable()
        {
            CleanupDoorGameObject();
        }

        #endregion Lifecycle Methods

        #region Public Methods

        private static void LoadSettings(ModSettings modSettings, ModSettingsChange change)
        {
            HideInteractTooltip = modSettings.GetBool("GeneralSettings", "HideDefaultInteractTooltip");
            ShowHiddenDoorsTooltip = modSettings.GetBool("GeneralSettings", "ShowHiddenDoorsTooltip");
            OnlyInInfoMode = modSettings.GetBool("GeneralSettings", "OnlyInInfoMode");
            NamesOfRestrainedFoes = modSettings.GetBool("GeneralSettings", "NamesOfRestrainedFoes");
            ShowLockLevel = modSettings.GetBool("Lockinfo", "ShowLockLevel");
            ShowLockStatus = modSettings.GetBool("Lockinfo", "ShowLockStatus");
            ShowLockOpenChance = modSettings.GetBool("Lockinfo", "ShowLockOpenChance");
            CenterText = modSettings.GetBool("Experimental", "CenterText");
            FontIndex = modSettings.GetInt("Experimental", "Font");
            Textured = modSettings.GetBool("Experimental", "Textured");
            BgColor = modSettings.GetColor("Experimental", "BackgroundColor");
            TextColor = modSettings.GetColor("Experimental", "TextColor");
            FontScale = modSettings.GetFloat("Experimental", "FontScale");

            // Tooltip positioning settings
            TooltipOffsetX = modSettings.GetInt("Positioning", "TooltipOffsetX");
            TooltipOffsetY = modSettings.GetInt("Positioning", "TooltipOffsetY");
        }

        public override void Draw()
        {
            base.Draw();
            tooltip.Draw();
        }

        public override void Update()
        {
            base.Update();

            if (OnlyInInfoMode && GameManager.Instance.PlayerActivate.CurrentMode != PlayerActivateModes.Info)
            {
                // Clean up door GameObject when not in info mode to prevent interference
                CleanupDoorGameObject();
                return;
            }

            // Only cleanup and hide tooltips on actual activation completion
            // This prevents the tooltip's collision detection from interfering with door opening
            // but allows tooltips to remain visible during mouse down
            if (InputManager.Instance.ActionComplete(InputManager.Actions.ActivateCenterObject))
            {
                CleanupDoorGameObject();
                return; // Don't show tooltips after successful activation
            }

            // Cleanup when activation starts to prevent interference, but allow tooltip to continue showing
            if (InputManager.Instance.ActionStarted(InputManager.Actions.ActivateCenterObject))
            {
                CleanupDoorGameObject();
            }

            // Also cleanup if player is moving quickly or changing direction rapidly
            // This helps prevent stale door GameObjects from persisting
            // Use input detection as a proxy for movement since PlayerMotor properties may vary
            if (InputManager.Instance.HasAction(InputManager.Actions.MoveForwards) ||
                InputManager.Instance.HasAction(InputManager.Actions.MoveBackwards) ||
                InputManager.Instance.HasAction(InputManager.Actions.MoveLeft) ||
                InputManager.Instance.HasAction(InputManager.Actions.MoveRight) ||
                InputManager.Instance.HasAction(InputManager.Actions.Run))
            {
                CleanupDoorGameObject();
            }

            tooltip.Scale = new Vector2(DaggerfallUI.Instance.DaggerfallHUD.NativePanel.LocalScale.x * FontScale, DaggerfallUI.Instance.DaggerfallHUD.NativePanel.LocalScale.y * FontScale);
            tooltip.AutoSize = AutoSizeModes.Scale;
            AutoSize = AutoSizeModes.None;

            var text = GetHoverText();

            if (string.IsNullOrEmpty(text))
            {
                // Clean up when no tooltip is being shown
                CleanupDoorGameObject();
                return;
            }

            tooltip.Draw(text);
        }

        /// <summary>
        /// Safely cleanup the door GameObject and collider to prevent interference with door opening
        /// </summary>
        private void CleanupDoorGameObject()
        {
            if (goDoor != null)
            {
                Object.Destroy(goDoor);
                goDoor = null;
                goDoorCollider = null;
                prevHit = null; // Reset hit tracking to force fresh detection
            }
        }

        #endregion Public Methods

        #region Custom Modding API

        private static void RegisterCustomTooltip(float activationDistance, Func<RaycastHit, string> func)
        {
            if (!customGetHoverText.TryGetValue(activationDistance, out _))
            {
                customGetHoverText[activationDistance] = new List<Func<RaycastHit, string>>();
            }

            customGetHoverText[activationDistance].Add(func);
            Debug.Log("************World Tooltips: Registered Custom Tooltip");
        }

        public static void MessageReceiver(string message, object data, DFModMessageCallback callback)
        {
            switch (message)
            {
                case REGISTER_CUSTOM_TOOLTIP:
                    if (data == null)
                    {
                        Debug.LogError("MessageReceiver: data object is null");
                        break;
                    }

                    if (!(data is System.Tuple<float, Func<RaycastHit, string>>))
                    {
                        Debug.LogError("MessageReceiver: data object is not of type 'System.Tuple<float, Func<RaycastHit, string>>'");
                        break;
                    }

                    var tup = (System.Tuple<float, Func<RaycastHit, string>>)data;

                    if (tup.Item2 == null)
                    {
                        Debug.LogError("MessageReceiver: data tuple's Func<RaycastHit, string> is null");
                        break;
                    }

                    RegisterCustomTooltip(tup.Item1, tup.Item2);
                    break;
                default:
                    Debug.LogError("MessageReceiver: invalid message");
                    break;
            }
        }

        #endregion Custom Modding API

        #region Private Methods

        private string EnumerateCustomHoverText(RaycastHit hit)
        {
            if (customGetHoverText.Count == 0)
            {
                return null;
            }

            string result = null;
            var stop = false;
            foreach (var kv in customGetHoverText.Where(kv => kv.Key >= hit.distance))
            {
                foreach (var func in kv.Value)
                {
                    result = func(hit);

                    if (string.IsNullOrEmpty(result))
                    {
                        continue;
                    }

                    prevDistance = kv.Key;
                    stop = true;
                    break;
                }

                if (stop)
                {
                    break;
                }
            }

            return result;
        }

        private static bool WithinPeepRange(RaycastHit hit)
        {
            // Only in the Info Mode!
            if (GameManager.Instance.PlayerActivate.CurrentMode != PlayerActivateModes.Info)
            {
                return false;
            }

            //Determine XZ distance from player to object
            Vector3 path = hit.point - GameManager.Instance.PlayerController.transform.position;
            var distanceXZ = Vector3.ProjectOnPlane(path, Vector3.up).magnitude;
            return distanceXZ <= 0.6f;
        }

        private static bool ThePeeperIsHere()
        {
            return GameObject.Find("Penwick Peeper");
        }

        private string GetHoverText()
        {
            Ray ray = new Ray(mainCamera.transform.position, mainCamera.transform.forward);

            // Did we hit something?
            if (!Physics.Raycast(ray, out var hit, rayDistance, playerLayerMask))
            {
                // Clean up when no hit is detected to prevent stale GameObjects
                CleanupDoorGameObject();
                return null;
            }

            // Skip processing if we hit our own temporary door GameObject
            // This prevents recursive issues and ensures clean operation
            if (goDoor != null && hit.transform == goDoor.transform)
            {
                CleanupDoorGameObject();
                return null;
            }

            var textIsSet = !string.IsNullOrEmpty(prevText);

            var sameDistance = Math.Abs(hit.distance - prevDistance) > 0.1f;

            var isSameObject = hit.transform == prevHit;

            var withinPeepRange = PenwickPeepIsOn && WithinPeepRange(hit);

            // The same object, with known text, the same distance.
            if (isSameObject && textIsSet && sameDistance && !withinPeepRange)
            {
                return prevText;
            }

            // Other object, or no text, or other distance.
            prevHit = hit.transform;

            var stop = false;

            var result = EnumerateCustomHoverText(hit);

            // Easy base cases
            if (string.IsNullOrEmpty(result))
            {
                if (hit.transform.name.Length > 16 && hit.transform.name.Substring(0, 17) == "DaggerfallTerrain")
                {
                    stop = true;
                }
            }

            if (stop)
            {
                prevHit = null;
                prevText = result;
                return result;
            }

            object comp;
            // Objects with "Mobile NPC" and "Static NPC" activation distances
            if (string.IsNullOrEmpty(result) && hit.distance <= PlayerActivate.MobileNPCActivationDistance)
            {
                if (CheckComponent<MobilePersonNPC>(hit, out comp))
                {
                    result = ((MobilePersonNPC)comp).NameNPC;
                    prevDistance = PlayerActivate.MobileNPCActivationDistance;
                }

                if (NamesOfRestrainedFoes && string.IsNullOrEmpty(result) && CheckComponent<QuestResourceBehaviour>(hit, out comp))
                {
                    if (((QuestResourceBehaviour)comp).TargetResource is Foe foe)
                    {
                        if (foe.IsRestrained)
                        {
                            foe.ExpandMacro(MacroTypes.DetailsMacro, out var str);
                            result = str;

                            prevDistance = PlayerActivate.MobileNPCActivationDistance;
                        }
                    }
                }

                if (string.IsNullOrEmpty(result) && CheckComponent<DaggerfallEntityBehaviour>(hit, out comp))
                {
                    var behaviour = (DaggerfallEntityBehaviour)comp;

                    var enemyMotor = behaviour.transform.GetComponent<EnemyMotor>();

                    if (behaviour.Entity is EnemyEntity enemyEntity)
                    {
                        if (enemyMotor && !enemyMotor.IsHostile)
                        {
                            result = enemyEntity.Name == enemyEntity.Career.Name
                                // Wasn't renamed
                                ? TextManager.Instance.GetLocalizedEnemyName(enemyEntity.MobileEnemy.ID)
                                // Was renamed
                                : enemyEntity.Name;

                            prevDistance = PlayerActivate.MobileNPCActivationDistance;
                        }
                    }
                }

                if (string.IsNullOrEmpty(result) && CheckComponent<DaggerfallBulletinBoard>(hit, out comp))
                {
                    result = Localize("BulletinBoard");
                    prevDistance = PlayerActivate.MobileNPCActivationDistance;
                }

                if (string.IsNullOrEmpty(result) && CheckComponent<StaticNPC>(hit, out comp))
                {
                    var npc = (StaticNPC)comp;

                    var factionID = 0;
                    try
                    {
                        factionID = npc.Data.factionID;
                    }
                    catch
                    {
                        // ignored
                    }

                    if (factionID > 0)
                    {
                        if (GameManager.Instance.PlayerEntity.FactionData.GetFactionData(factionID, out var factionData))
                        {
                            // This must be an individual NPC or Daedra
                            if (factionData.type == (int)FactionFile.FactionTypes.Individual ||
                                factionData.type == (int)FactionFile.FactionTypes.Daedra)
                            {
                                result = TextManager.Instance.GetLocalizedFactionName(factionID, npc.DisplayName);
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(result))
                    {
                        result = npc.DisplayName;
                    }

                    prevDistance = PlayerActivate.StaticNPCActivationDistance;
                }
            }

            // Checking for loot
            // Corpses have a different activation distance than other containers/loot
            if (string.IsNullOrEmpty(result) && hit.distance <= PlayerActivate.CorpseActivationDistance)
            {
                if (CheckComponent<DaggerfallLoot>(hit, out comp))
                {
                    var loot = (DaggerfallLoot)comp;

                    if (loot.ContainerType == LootContainerTypes.CorpseMarker)
                    {
                        result = string.Format(Localize("DeadEnemy"), loot.entityName);
                        prevDistance = PlayerActivate.CorpseActivationDistance;
                    }
                }
            }

            // DefaultActivationDistance == DoorActivationDistance == TreasureActivationDistance == PickpocketDistance
            if (string.IsNullOrEmpty(result) && hit.distance <= PlayerActivate.DefaultActivationDistance)
            {
                // Checking for normal loot
                if (CheckComponent<DaggerfallLoot>(hit, out comp))
                {
                    var loot = (DaggerfallLoot)comp;

                    switch (loot.ContainerType)
                    {
                        case LootContainerTypes.DroppedLoot:
                        case LootContainerTypes.RandomTreasure:
                            if (loot.Items.Count == 1)
                            {
                                var item = loot.Items.GetItem(0);
                                result = item.LongName;

                                if (item.stackCount > 1)
                                {
                                    result = string.Format(Localize("StackCount"), result, item.stackCount);
                                }
                            }
                            else
                            {
                                result = Localize("LootPile");
                            }

                            break;
                        case LootContainerTypes.ShopShelves:
                            result = Localize("ShopShelf");
                            break;
                        case LootContainerTypes.HouseContainers:
                            var mesh = hit.transform.GetComponent<MeshFilter>();
                            if (mesh)
                            {
                                int record;
                                if (TryExtractNumber(mesh.name, out record))
                                {
                                    switch (record)
                                    {
                                        case 41003:
                                        case 41004:
                                        case 41800:
                                        case 41801:
                                            result = Localize("Wardrobe");
                                            break;
                                        case 41007:
                                        case 41008:
                                        case 41033:
                                        case 41038:
                                        case 41805:
                                        case 41810:
                                        case 41802:
                                            result = Localize("Cabinets");
                                            break;
                                        case 41027:
                                            result = Localize("Shelf");
                                            break;
                                        case 41034:
                                        case 41050:
                                        case 41803:
                                        case 41806:
                                            result = Localize("Dresser");
                                            break;
                                        case 41032:
                                        case 41035:
                                        case 41037:
                                        case 41051:
                                        case 41807:
                                        case 41804:
                                        case 41808:
                                        case 41809:
                                        case 41814:
                                            result = Localize("Cupboard");
                                            break;
                                        case 41815:
                                        case 41816:
                                        case 41817:
                                        case 41818:
                                        case 41819:
                                        case 41820:
                                        case 41821:
                                        case 41822:
                                        case 41823:
                                        case 41824:
                                        case 41825:
                                        case 41826:
                                        case 41827:
                                        case 41828:
                                        case 41829:
                                        case 41830:
                                        case 41831:
                                        case 41832:
                                        case 41833:
                                        case 41834:
                                            result = Localize("Crate");
                                            break;
                                        case 41811:
                                        case 41812:
                                        case 41813:
                                            result = Localize("Chest");
                                            break;
                                        default:
                                            result = Localize("Interact");
                                            break;
                                    }
                                }
                            }

                            break;
                    }

                    prevDistance = PlayerActivate.TreasureActivationDistance;
                }

                if (string.IsNullOrEmpty(result) && CheckComponent<DaggerfallAction>(hit, out comp))
                {
                    var da = (DaggerfallAction)comp;
                    if (da.TriggerFlag == DFBlock.RdbTriggerFlags.Direct ||
                        da.TriggerFlag == DFBlock.RdbTriggerFlags.Direct6 ||
                        da.TriggerFlag == DFBlock.RdbTriggerFlags.MultiTrigger)
                    {
                        var multiTriggerOkay = false;
                        var mesh = hit.transform.GetComponent<MeshFilter>();
                        if (mesh)
                        {
                            int record;
                            if (TryExtractNumber(mesh.name, out record))
                            {
                                switch (record)
                                {
                                    case 1:
                                        result = Localize("Wheel");
                                        multiTriggerOkay = true;
                                        break;
                                    case 61027:
                                    case 61028:
                                        result = Localize("Lever");
                                        multiTriggerOkay = true;
                                        break;
                                    case 74143:
                                        result = Localize("Mantella");
                                        break;
                                    case 62323:
                                    // Secret teleport
                                    case 72019:
                                    case 74215:
                                    case 74225:
                                        multiTriggerOkay = true;
                                        break;
                                }
                            }
                        }

                        if (da.TriggerFlag == DFBlock.RdbTriggerFlags.MultiTrigger && !multiTriggerOkay)
                        {
                            result = null;
                        }
                        else
                        {
                            if (!HideInteractTooltip && string.IsNullOrEmpty(result))
                            {
                                result = Localize("Interact");
                            }

                            prevDistance = PlayerActivate.DefaultActivationDistance;
                        }
                    }
                }

                if (string.IsNullOrEmpty(result) && CheckComponent<DaggerfallLadder>(hit, out comp))
                {
                    result = Localize("Ladder");
                    prevDistance = PlayerActivate.DefaultActivationDistance;
                }

                if (string.IsNullOrEmpty(result) && CheckComponent<DaggerfallBookshelf>(hit, out comp))
                {
                    result = Localize("Bookshelf");
                    prevDistance = PlayerActivate.DefaultActivationDistance;
                }

                if (string.IsNullOrEmpty(result) && CheckComponent<QuestResourceBehaviour>(hit, out comp))
                {
                    var qrb = (QuestResourceBehaviour)comp;

                    if (qrb.TargetResource is Item)
                    {
                        if (CheckComponent<DaggerfallBillboard>(hit, out comp))
                        {
                            var bb = (DaggerfallBillboard)comp;
                            var archive = bb.Summary.Archive;
                            var index = bb.Summary.Record;

                            if (archive == 211)
                            {
                                switch (index)
                                {
                                    case 54:
                                        result = Localize("TotemOfSeptim");
                                        break;
                                }
                            }
                        }

                        if (string.IsNullOrEmpty(result))
                        {
                            result = DaggerfallUnity.Instance.ItemHelper.ResolveItemLongName(((Item)qrb.TargetResource).DaggerfallUnityItem, false);
                        }

                        prevDistance = PlayerActivate.DefaultActivationDistance;
                    }
                }

                if (string.IsNullOrEmpty(result) && CheckComponent<DaggerfallActionDoor>(hit, out comp))
                {
                    var door = (DaggerfallActionDoor)comp;

                    if (PenwickPeepIsOn && withinPeepRange && door.IsClosed)
                    {
                        if (ThePeeperIsHere())
                        {
                            return result;
                        }
                    }

                    if (TryExtractNumber(door.name, out var record) && !ShowHiddenDoorsTooltip)
                    {
                        switch (record)
                        {
                            case 55006:
                            case 55007:
                            case 55008:
                            case 55009:
                            case 55010:
                            case 55011:
                            case 55012:
                            case 55017:
                            case 55018:
                            case 55019:
                            case 55020:
                            case 55021:
                            case 55022:
                            case 55023:
                            case 55024:
                            case 55025:
                            case 55026:
                            case 55027:
                            case 55028:
                            case 55029:
                            case 55030:
                            case 55031:
                            case 55032:
                            case 72100:
                                result = "";
                                return result;
                        }
                    }

                    result = Localize("Door");

                    if (door.IsLocked && ShowLockStatus)
                    {
                        result = ShowLockLevel
                            ? string.Format(Localize("LockLevel"), result, door.CurrentLockValue)
                            : string.Format(Localize("LockLevelHidden"), result);

                        if (ShowLockOpenChance)
                        {
                            var player = GameManager.Instance.PlayerEntity;
                            var chance = FormulaHelper.CalculateInteriorLockpickingChance(player.Level, door.CurrentLockValue, player.Skills.GetLiveSkillValue(DFCareer.Skills.Lockpicking));

                            result = string.Format(Localize("LockLevelUnlockChance"), result, chance);
                        }
                    }

                    prevDistance = PlayerActivate.DoorActivationDistance;
                }

                // "else", look for static doors on this object. If there are any, return the specific location
                // Is computationally expensive and should be saved for last
                if (string.IsNullOrEmpty(result))
                {
                    Transform doorOwner;
                    DaggerfallStaticDoors doors = GetDoors(hit.transform, out doorOwner);
                    if (doors)
                    {
                        result = GetStaticDoorText(doors, hit, doorOwner);
                        prevDistance = PlayerActivate.DoorActivationDistance;
                    }
                    else
                    {
                        prevHit = null;
                    }
                }
            }

            prevText = result;

            return result;
        }

        private string GetStaticDoorText(DaggerfallStaticDoors doors, RaycastHit hit, Transform _)
        {
            StaticDoor door;

            // Removed undefined CustomDoor.HasHit reference that could cause compilation errors
            if (hit.distance <= PlayerActivate.DoorActivationDistance && HasHit(doors, hit.point, out door))
            {
                switch (door.doorType)
                {
                    case DoorTypes.Building when !playerEnterExit.IsPlayerInside:
                    {
                        // Check for a static building hit
                        StaticBuilding building;
                        DFLocation.BuildingTypes buildingType;
                        bool buildingLocked;
                        int buildingLockValue;

                        DaggerfallStaticBuildings buildings = GetBuildings(hit.transform);
                        if (!buildings || !buildings.HasHit(hit.point, out building))
                        {
                            return prevDoorText;
                        }

                        // Get building directory for location
                        BuildingDirectory buildingDirectory = GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory();
                        if (!buildingDirectory)
                        {
                            return "<ERR: 010>";
                        }

                        // Get detailed building data from directory
                        BuildingSummary buildingSummary;
                        if (!buildingDirectory.GetBuildingSummary(building.buildingKey, out buildingSummary))
                        {
                            return "<ERR: 011>";
                        }

                        buildingLocked = !playerActivate.BuildingIsUnlocked(buildingSummary);
                        buildingLockValue = playerActivate.GetBuildingLockValue(buildingSummary);
                        buildingType = buildingSummary.BuildingType;

                        // Discover building
                        playerGPS.DiscoverBuilding(building.buildingKey);

                        // Get discovered building
                        PlayerGPS.DiscoveredBuilding db;
                        if (!playerGPS.GetDiscoveredBuilding(building.buildingKey, out db))
                        {
                            return prevDoorText;
                        }

                        var doorText = buildingType == DFLocation.BuildingTypes.Town23
                            ? string.Format(Localize("GoToCityWalls"), playerGPS.CurrentLocalizedLocationName)
                            : string.Format(Localize("GoTo"), db.displayName);

                        if (buildingLocked && ShowLockStatus)
                        {
                            doorText = ShowLockLevel
                                ? string.Format(Localize("LockLevel"), doorText, buildingLockValue)
                                : string.Format(Localize("LockLevelHidden"), doorText);

                            if (ShowLockOpenChance)
                            {
                                var player = GameManager.Instance.PlayerEntity;
                                var chance = FormulaHelper.CalculateInteriorLockpickingChance(player.Level, buildingLockValue, player.Skills.GetLiveSkillValue(DFCareer.Skills.Lockpicking));

                                doorText = string.Format(Localize("LockLevelUnlockChance"), doorText, chance);
                            }
                        }

                        if (buildingLocked
                            && buildingType <= DFLocation.BuildingTypes.Palace
                            && buildingType != DFLocation.BuildingTypes.HouseForSale)
                        {
                            var buildingClosedMessage = (buildingType == DFLocation.BuildingTypes.GuildHall)
                                ? TextManager.Instance.GetLocalizedText("guildClosed")
                                : TextManager.Instance.GetLocalizedText("storeClosed");

                            if (buildingType == DFLocation.BuildingTypes.Palace)
                            {
                                buildingClosedMessage = Localize("PalaceClosed");
                            }

                            buildingClosedMessage = buildingClosedMessage.Replace("%d1", openHours[(int)buildingType].ToString());
                            buildingClosedMessage = buildingClosedMessage.Replace("%d2", closeHours[(int)buildingType].ToString());
                            doorText += "\r" + buildingClosedMessage;
                        }

                        prevDoorText = doorText;

                        return doorText;

                        //If we caught ourselves hitting the same door again directly without touching the building, just return the previous text which should be the door's
                    }
                    case DoorTypes.Building when playerEnterExit.IsPlayerInside:
                        // Hit door while inside, transition outside
                        return string.Format(Localize("GoTo"), playerGPS.CurrentLocalizedLocationName);
                    case DoorTypes.DungeonEntrance when !playerEnterExit.IsPlayerInside:
                        // Hit dungeon door while outside, transition inside
                        return string.Format(Localize("GoTo"), playerGPS.CurrentLocalizedLocationName);
                    case DoorTypes.DungeonExit when playerEnterExit.IsPlayerInside:
                    {
                        // Hit dungeon exit while inside, ask if access wagon or transition outside
                        if (playerGPS.CurrentLocationType == DFRegion.LocationTypes.TownCity
                            || playerGPS.CurrentLocationType == DFRegion.LocationTypes.TownHamlet
                            || playerGPS.CurrentLocationType == DFRegion.LocationTypes.TownVillage)
                        {
                            return string.Format(Localize("GoTo"), playerGPS.CurrentLocalizedLocationName);
                        }

                        return string.Format(Localize("GoToRegion"), playerGPS.CurrentLocalizedRegionName);
                    }
                }
            }

            prevHit = null;

            return null;
        }

        private class DoorData
        {
            public Transform Parent;
            public Vector3 Position;
            public Quaternion Rotation;

            public float minX;
            public float minY;
            public float minZ;
            public float maxX;
            public float maxY;
            public float maxZ;

            public DoorData(StaticDoor door, DaggerfallStaticDoors dfuStaticDoors)
            {
                Quaternion buildingRotation = GameObjectHelper.QuaternionFromMatrix(door.buildingMatrix);
                Vector3 doorNormal = buildingRotation * door.normal;
                Quaternion facingRotation = Quaternion.LookRotation(doorNormal, Vector3.up);

                // Setup single trigger position and size over each door in turn
                // This method plays nice with transforms
                var transform = dfuStaticDoors.transform;
                Parent = transform;
                Position = transform.rotation * door.buildingMatrix.MultiplyPoint3x4(door.centre);
                Position += dfuStaticDoors.transform.position;
                Rotation = facingRotation;
            }

            public void SetMinMax(Vector3 min, Vector3 max)
            {
                minX = min.x;
                minY = min.y;
                minZ = min.z;
                maxX = max.x;
                maxY = max.y;
                maxZ = max.z;
            }
        }

        /// <summary>
        /// Check for a door hit in world space.
        /// Enhanced to minimize interference with door opening functionality.
        /// </summary>
        /// <param name="dfuStaticDoors">dfuStaticDoors</param>
        /// <param name="point">Hit point from ray test in world space.</param>
        /// <param name="doorOut">StaticDoor out if hit found.</param>
        /// <returns>True if point hits a static door.</returns>
        public bool HasHit(DaggerfallStaticDoors dfuStaticDoors, Vector3 point, out StaticDoor doorOut)
        {
            //Debug.Log("HasHit started");
            doorOut = new StaticDoor();

            if (dfuStaticDoors.Doors == null)
            {
                return false;
            }

            var doors = dfuStaticDoors.Doors;

            // Create GameObject with complete isolation from door interaction
            // Using a single hidden trigger created when testing door positions
            if (goDoor == null)
            {
                goDoor = new GameObject("WorldTooltips_TempDoorDetector");
                goDoor.hideFlags = HideFlags.HideAndDontSave;

                // Don't parent to dfuStaticDoors to avoid click interference
                // Instead, position it manually and destroy immediately after use
                goDoorCollider = goDoor.AddComponent<BoxCollider>();
                goDoorCollider.isTrigger = true;

                // Multiple layers of isolation
                goDoor.layer = LayerMask.NameToLayer("Ignore Raycast");
                goDoor.tag = "Untagged"; // Ensure it doesn't match any interaction tags

                // Disable all possible interaction components
                goDoor.SetActive(false); // Start inactive, only activate during bounds check
            }

            BoxCollider c = goDoorCollider;
            var found = false;

            // Check if we can reuse previous detection (but still clean up properly)
            if (goDoor && prevHit == goDoor.transform && c.bounds.Contains(point))
            {
                //Debug.Log("EARLY FOUND");
                found = true;
                doorOut = prevDoor;
            }

            // Test each door in array
            for (var i = 0; !found && i < doors.Length; i++)
            {
                var hash = 23;
                unchecked
                {
                    hash = hash * 31 + doors[i].buildingKey;
                    hash = hash * 31 + doors[i].blockIndex;
                    hash = hash * 31 + doors[i].recordIndex;
                    hash = hash * 31 + doors[i].doorIndex;
                    hash = hash * 31 + i;
                }

                DoorData doorData;
                var created = false;
                if (!doorDataDict.TryGetValue(hash, out doorData))
                {
                    doorData = doorDataDict[hash] = new DoorData(doors[i], dfuStaticDoors);
                    created = true;
                }

                //Debug.Log("DOORS ITERATE"+i+", hash:" + hash);

                // Setup trigger without parenting to avoid click interference
                // Position manually in world space instead of using parent hierarchy
                c.size = doors[i].size;

                // Don't parent - set world position directly to avoid hierarchy interference
                goDoor.transform.position = doorData.Position;
                goDoor.transform.rotation = doorData.Rotation;

                // Temporarily activate only for bounds calculation, then deactivate immediately
                goDoor.SetActive(true);

                // Has to be after setting position and rotation
                if (created)
                {
                    var bounds = c.bounds;
                    doorData.SetMinMax(bounds.min, bounds.max);
                }

                // Immediately deactivate to prevent any interaction
                goDoor.SetActive(false);

                // Check if hit was inside trigger
                // Much more performant
                if (point.x >= doorData.minX
                    && point.x < doorData.maxX
                    && point.y >= doorData.minY
                    && point.y < doorData.maxY
                    && point.z >= doorData.minZ
                    && point.z < doorData.maxZ
                   )
                {
                    //Debug.Log("HasHit FOUND");
                    found = true;
                    doorOut = doors[i];
                    if (doorOut.doorType == DoorTypes.DungeonExit)
                    {
                        break;
                    }
                }
            }

            // Cleanup strategy: Destroy if no door found, but allow brief persistence if found
            // This allows smoother tooltip display while still preventing click interference
            if (!found && goDoor != null)
            {
                Object.Destroy(goDoor);
                goDoor = null;
                goDoorCollider = null;
                prevHit = null;
            }
            else if (found && goDoor != null)
            {
                // Allow brief persistence for smooth tooltip, but ensure it's isolated
                prevHit = goDoor.transform;
                prevDoor = doorOut;

                // Schedule cleanup for a very short time to prevent click interference
                // The Update method will handle more aggressive cleanup when needed
                Object.Destroy(goDoor, 0.05f);
            }

            return found;
        }

        private bool CheckComponent<T>(RaycastHit hit, out object obj)
        {
            obj = hit.transform.GetComponent<T>();
            return obj != null;
        }

        // Look for doors on object, then on direct parent
        private DaggerfallStaticDoors GetDoors(Transform doorsTransform, out Transform owner)
        {
            owner = null;

            if (doorsTransform == prevDoorCheckTransform)
            {
                owner = prevDoorOwner;
                return prevStaticDoors;
            }

            DaggerfallStaticDoors doors = doorsTransform.GetComponent<DaggerfallStaticDoors>();
            if (!doors)
            {
                doors = doorsTransform.GetComponentInParent<DaggerfallStaticDoors>();
                if (doors)
                {
                    owner = doors.transform;
                }
            }
            else
            {
                owner = doors.transform;
            }

            prevDoorCheckTransform = doorsTransform;
            prevStaticDoors = doors;
            prevDoorOwner = owner;

            return doors;
        }

        // Look for building array on object, then on direct parent
        private DaggerfallStaticBuildings GetBuildings(Transform buildingsTransform)
        {
            DaggerfallStaticBuildings buildings = buildingsTransform.GetComponent<DaggerfallStaticBuildings>();
            if (!buildings)
            {
                buildings = buildingsTransform.GetComponentInParent<DaggerfallStaticBuildings>();
            }

            return buildings;
        }

        public class HUDTooltip : BaseScreenComponent
        {
            #region Fields

            private bool bordersSet;
            private Texture2D fillBordersTexture;
            private Texture2D topBorderTexture, bottomBorderTexture;
            private Texture2D leftBorderTexture, rightBorderTexture;
            private Texture2D topLeftBorderTexture, topRightBorderTexture;
            private Texture2D bottomLeftBorderTexture, bottomRightBorderTexture;

            private Rect lastDrawRect;
            private Rect fillBordersRect;
            private Rect topLeftBorderRect;
            private Rect topRightBorderRect;
            private Rect bottomLeftBorderRect;
            private Rect bottomRightBorderRect;
            private Rect topBorderRect;
            private Rect leftBorderRect;
            private Rect rightBorderRect;
            private Rect bottomBorderRect;

            public bool EnableBorder { get; set; }
            private Border<Vector2Int> virtualSizes;

            private const int _defaultMarginSize = 2;
            private const int _bottomMarginSize = 1;

            private int currentCursorHeight = -1;
            private int currentSystemHeight;
            private int currentRenderingHeight;
            private bool currentFullScreen;

            private bool drawToolTip;
            private string[] textRows;
            private float widestRow;
            private string lastText = string.Empty;
            private bool previousSDFState;

            #endregion

            #region Properties

            /// <summary>
            /// Gets or sets font used inside tooltip.
            /// </summary>
            public DaggerfallFont Font { get; set; }

            /// <summary>
            /// Gets or sets tooltip draw position relative to mouse.
            /// </summary>
            public Vector2 MouseOffset { get; set; } = Vector2.zero;

            #endregion

            #region Constructors

            public HUDTooltip()
            {
                if (Textured)
                {
                    SetBorderTextures(
                        TooltipBgTopLeft,
                        TooltipBgTop,
                        TooltipBgTopRight,
                        TooltipBgLeft,
                        TooltipBgFill,
                        TooltipBgRight,
                        TooltipBgBottomLeft,
                        TooltipBgBottom,
                        TooltipBgBottomRight,
                        FilterMode.Point
                    );
                }
                else
                {
                    BackgroundColor = BgColor;
                }

                SetMargins(Margins.Top, _defaultMarginSize);
                SetMargins(Margins.Bottom, _bottomMarginSize * 3);
                SetMargins(Margins.Left, _defaultMarginSize * 2);
                SetMargins(Margins.Right, _defaultMarginSize * 2);
            }

            #endregion

            #region Public Methods

            public void SetBorderTextures(
                Texture2D topLeft,
                Texture2D top,
                Texture2D topRight,
                Texture2D left,
                Texture2D fill,
                Texture2D right,
                Texture2D bottomLeft,
                Texture2D bottom,
                Texture2D bottomRight,
                FilterMode filterMode,
                Border<Vector2Int>? virtualSizes = null)
            {
                // Save texture references
                topLeftBorderTexture = topLeft;
                topBorderTexture = top;
                topRightBorderTexture = topRight;
                leftBorderTexture = left;
                fillBordersTexture = fill;
                rightBorderTexture = right;
                bottomLeftBorderTexture = bottomLeft;
                bottomBorderTexture = bottom;
                bottomRightBorderTexture = bottomRight;

                // Set texture filtering
                topLeftBorderTexture.filterMode = filterMode;
                topBorderTexture.filterMode = filterMode;
                topRightBorderTexture.filterMode = filterMode;
                leftBorderTexture.filterMode = filterMode;
                fillBordersTexture.filterMode = filterMode;
                rightBorderTexture.filterMode = filterMode;
                bottomLeftBorderTexture.filterMode = filterMode;
                bottomBorderTexture.filterMode = filterMode;
                bottomRightBorderTexture.filterMode = filterMode;

                // Set texture wrap modes
                topBorderTexture.wrapMode = TextureWrapMode.Repeat;
                bottomBorderTexture.wrapMode = TextureWrapMode.Repeat;
                leftBorderTexture.wrapMode = TextureWrapMode.Repeat;
                rightBorderTexture.wrapMode = TextureWrapMode.Repeat;
                fillBordersTexture.wrapMode = TextureWrapMode.Repeat;

                // Set flags
                bordersSet = true;
                EnableBorder = true;

                // Set sizes
                this.virtualSizes = virtualSizes ?? new Border<Vector2Int>
                {
                    TopLeft = new Vector2Int(topLeft.width, topLeft.height),
                    Top = new Vector2Int(top.width, top.height),
                    TopRight = new Vector2Int(topRight.width, topRight.height),
                    Left = new Vector2Int(left.width, left.height),
                    Fill = new Vector2Int(fill.width, fill.height),
                    Right = new Vector2Int(right.width, right.height),
                    BottomLeft = new Vector2Int(bottomLeft.width, bottomLeft.height),
                    Bottom = new Vector2Int(bottom.width, bottom.height),
                    BottomRight = new Vector2Int(bottomRight.width, bottomRight.height),
                };
            }

            public override void Update()
            {
                base.Update();
                if (DaggerfallUnity.Settings.CursorHeight != currentCursorHeight ||
                    Display.main.systemHeight != currentSystemHeight ||
                    Display.main.renderingHeight != currentRenderingHeight ||
                    DaggerfallUnity.Settings.Fullscreen != currentFullScreen)
                {
                    UpdateMouseOffset();
                }
            }

            private void UpdateMouseOffset()
            {
                currentCursorHeight = DaggerfallUnity.Settings.CursorHeight;
                currentSystemHeight = Display.main.systemHeight;
                currentRenderingHeight = Display.main.renderingHeight;
                currentFullScreen = DaggerfallUnity.Settings.Fullscreen;
                MouseOffset = Vector2.zero;
            }

            /// <summary>
            /// Get screen center coordinates that work properly with RetroRenderer and large HUD
            /// </summary>
            private Vector2 GetScreenCenter()
            {
                // Always use actual screen coordinates - let RetroRenderer handle the transformation
                // The tooltip is part of the HUD system, so RetroRenderer will scale it appropriately

                float centerX = Screen.width * 0.5f;
                float centerY = Screen.height * 0.5f;

                // Adjust for large HUD if docked - the crosshair is in the center of the remaining viewport
                if (DaggerfallUI.Instance.DaggerfallHUD != null &&
                    DaggerfallUnity.Settings.LargeHUD &&
                    DaggerfallUnity.Settings.LargeHUDDocked)
                {
                    HUDLarge largeHUD = DaggerfallUI.Instance.DaggerfallHUD.LargeHUD;
                    // The game viewport is reduced by the HUD height, so crosshair moves up
                    float availableHeight = Screen.height - largeHUD.Rectangle.height;
                    centerY = availableHeight * 0.5f; // Center of the remaining viewport area
                }

                return new Vector2(centerX, centerY);
            }

            /// <summary>
            /// Flags tooltip to be drawn at end of UI update.
            /// </summary>
            /// <param name="text">Text to render inside tooltip.</param>
            public void Draw(string text)
            {
                switch (FontIndex)
                {
                    case 0:
                        Font = DaggerfallUI.LargeFont;
                        break;
                    case 1:
                        Font = DaggerfallUI.TitleFont;
                        break;
                    case 2:
                        Font = DaggerfallUI.SmallFont;
                        break;
                    case 3:
                        Font = DaggerfallUI.DefaultFont;
                        break;
                }

                // Validate
                if (Font == null || string.IsNullOrEmpty(text))
                {
                    drawToolTip = false;
                    return;
                }

                // Update text rows
                UpdateTextRows(text);
                if (textRows == null || textRows.Length == 0)
                {
                    drawToolTip = false;
                    return;
                }

                // Set tooltip size
                Size = new Vector2(
                    widestRow + LeftMargin + RightMargin,
                    Font.GlyphHeight * textRows.Length + TopMargin + BottomMargin - 1);

                // Position tooltip relative to crosshair - let RetroRenderer handle scaling
                Vector2 screenCenter = GetScreenCenter();

                // Position tooltip relative to crosshair (screen center) with offset to avoid covering the target
                Vector2 tooltipPosition = new Vector2(
                    screenCenter.x + TooltipOffsetX,
                    screenCenter.y + TooltipOffsetY
                );

                // Ensure tooltip stays within screen bounds (accounting for large HUD)
                float maxY = Screen.height;
                if (DaggerfallUI.Instance.DaggerfallHUD != null &&
                    DaggerfallUnity.Settings.LargeHUD &&
                    DaggerfallUnity.Settings.LargeHUDDocked)
                {
                    HUDLarge largeHUD = DaggerfallUI.Instance.DaggerfallHUD.LargeHUD;
                    maxY = Screen.height - largeHUD.Rectangle.height; // Don't overlap with HUD
                }

                if (tooltipPosition.x + Size.x > Screen.width)
                    tooltipPosition.x = screenCenter.x - Size.x - Math.Abs(TooltipOffsetX); // Move to left of crosshair

                if (tooltipPosition.y < 0)
                    tooltipPosition.y = 10; // Move to top of screen

                if (tooltipPosition.y + Size.y > maxY)
                    tooltipPosition.y = maxY - Size.y - 10; // Move up from bottom (or HUD)

                Position = tooltipPosition;

                // Check if mouse position is in parent's rectangle (to prevent tooltips out of panel's rectangle to be displayed)
                if (Parent != null)
                {
                    // Raise flag to draw tooltip
                    drawToolTip = true;
                }
            }

            public override void Draw()
            {
                if (!Enabled)
                {
                    return;
                }

                if (drawToolTip)
                {
                    base.Draw();

                    // Set render area for tooltip to whole screen - RetroRenderer handles the rest
                    Material material = Font.GetMaterial();
                    Vector4 scissorRect = new Vector4(0, 1, 0, 1);
                    material.SetVector(_scissorRect, scissorRect);

                    // Determine text position
                    Rect rect = Rectangle;
                    Vector2 textPos = new Vector2(
                        rect.x + LeftMargin * Scale.x,
                        rect.y + TopMargin * Scale.y);

                    // Draw border
                    if (EnableBorder && bordersSet)
                    {
                        DrawBorder();
                    }

                    // Draw tooltip text
                    foreach (var textRow in textRows)
                    {
                        if (CenterText)
                        {
                            var temp = textPos.x;
                            var calc = Font.CalculateTextWidth(textRow, Scale);
                            textPos.x = rect.x + (widestRow - calc) / 2 * Scale.x + LeftMargin * Scale.x;
                            Font.DrawText(textRow, textPos, Scale, TextColor);
                            textPos.y += Font.GlyphHeight * Scale.y;
                            textPos.x = temp;
                        }
                        else
                        {
                            Font.DrawText(textRow, textPos, Scale, TextColor);
                            textPos.y += Font.GlyphHeight * Scale.y;
                        }
                    }

                    // Lower flag
                    drawToolTip = false;
                }
            }

            #endregion

            #region Private Methods

            private void UpdateTextRows(string text)
            {
                // Do nothing if text has not changed since last time
                var sdfState = Font.IsSDFCapable;
                if (text == lastText && sdfState == previousSDFState)
                {
                    return;
                }

                // Split into rows based on \r escape character
                // Text read from plain-text files will become \\r so need to replace this first
                text = text.Replace("\\r", "\r");
                textRows = text.Split('\r');

                // Set text we just processed
                lastText = text;

                // Find the widest row
                widestRow = 0;
                foreach (var textRow in textRows)
                {
                    var width = Font.CalculateTextWidth(textRow, Scale);
                    if (width > widestRow)
                    {
                        widestRow = width;
                    }
                }

                previousSDFState = sdfState;
            }

            private void DrawBorder()
            {
                Rect drawRect = Rectangle;
                if (drawRect != lastDrawRect)
                {
                    UpdateBorderDrawRects(drawRect);
                    lastDrawRect = drawRect;
                }

                // Draw fill
                DaggerfallUI.DrawTextureWithTexCoords(fillBordersRect, fillBordersTexture, new Rect(0, 0, (fillBordersRect.width / (LocalScale.x * 1.4f)) / virtualSizes.Fill.x, (fillBordersRect.height / (LocalScale.y * 1.4f)) / virtualSizes.Fill.y));

                // Draw corners
                DaggerfallUI.DrawTexture(topLeftBorderRect, topLeftBorderTexture);
                DaggerfallUI.DrawTexture(topRightBorderRect, topRightBorderTexture);
                DaggerfallUI.DrawTexture(bottomLeftBorderRect, bottomLeftBorderTexture);
                DaggerfallUI.DrawTexture(bottomRightBorderRect, bottomRightBorderTexture);

                // Draw edges
                DaggerfallUI.DrawTextureWithTexCoords(topBorderRect, topBorderTexture, new Rect(0, 0, (topBorderRect.width / (LocalScale.x * 1.4f)) / virtualSizes.Top.x, 1));
                DaggerfallUI.DrawTextureWithTexCoords(leftBorderRect, leftBorderTexture, new Rect(0, 0, 1, (leftBorderRect.height / (LocalScale.y * 1.4f)) / virtualSizes.Left.y));
                DaggerfallUI.DrawTextureWithTexCoords(rightBorderRect, rightBorderTexture, new Rect(0, 0, 1, (rightBorderRect.height / (LocalScale.y * 1.4f)) / virtualSizes.Right.y));
                DaggerfallUI.DrawTextureWithTexCoords(bottomBorderRect, bottomBorderTexture, new Rect(0, 0, (bottomBorderRect.width / (LocalScale.y * 1.4f)) / virtualSizes.Bottom.x, 1));
            }

            private void UpdateBorderDrawRects(Rect drawRect)
            {
                // Round input rectangle to pixel coordinates
                drawRect.x = Mathf.Round(drawRect.x);
                drawRect.y = Mathf.Round(drawRect.y);
                drawRect.xMax = Mathf.Round(drawRect.xMax);
                drawRect.yMax = Mathf.Round(drawRect.yMax);

                // Top-left
                topLeftBorderRect.x = drawRect.x;
                topLeftBorderRect.y = drawRect.y;
                topLeftBorderRect.xMax = Mathf.Round(drawRect.x + virtualSizes.TopLeft.x * (LocalScale.x * 1.4f));
                topLeftBorderRect.yMax = Mathf.Round(drawRect.y + virtualSizes.TopLeft.y * (LocalScale.y * 1.4f));

                // Top-right
                topRightBorderRect.x = Mathf.Round(drawRect.xMax - virtualSizes.TopRight.x * (LocalScale.x * 1.4f));
                topRightBorderRect.y = drawRect.y;
                topRightBorderRect.xMax = drawRect.xMax;
                topRightBorderRect.yMax = Mathf.Round(drawRect.y + virtualSizes.TopRight.y * (LocalScale.y * 1.4f));

                // Bottom-left
                bottomLeftBorderRect.x = drawRect.x;
                bottomLeftBorderRect.y = Mathf.Round(drawRect.yMax - virtualSizes.BottomLeft.x * (LocalScale.y * 1.4f));
                bottomLeftBorderRect.xMax = Mathf.Round(drawRect.x + virtualSizes.BottomLeft.x * (LocalScale.x * 1.4f));
                bottomLeftBorderRect.yMax = drawRect.yMax;

                // Bottom-right
                bottomRightBorderRect.x = Mathf.Round(drawRect.xMax - virtualSizes.BottomRight.x * (LocalScale.x * 1.4f));
                bottomRightBorderRect.y = Mathf.Round(drawRect.yMax - virtualSizes.BottomRight.y * (LocalScale.y * 1.4f));
                bottomRightBorderRect.xMax = drawRect.xMax;
                bottomRightBorderRect.yMax = drawRect.yMax;

                // Top
                topBorderRect.x = Mathf.Round(drawRect.x + virtualSizes.TopLeft.x * (LocalScale.x * 1.4f));
                topBorderRect.y = drawRect.y;
                topBorderRect.xMax = Mathf.Round(drawRect.xMax - virtualSizes.TopRight.x * (LocalScale.x * 1.4f));
                topBorderRect.yMax = Mathf.Round(drawRect.y + virtualSizes.Top.y * (LocalScale.y * 1.4f));

                // Left
                leftBorderRect.x = drawRect.x;
                leftBorderRect.y = Mathf.Round(drawRect.y + virtualSizes.TopLeft.y * (LocalScale.y * 1.4f));
                leftBorderRect.xMax = Mathf.Round(drawRect.x + virtualSizes.Left.x * (LocalScale.x * 1.4f));
                leftBorderRect.yMax = Mathf.Round(drawRect.yMax - virtualSizes.BottomLeft.y * (LocalScale.y * 1.4f));

                // Right
                rightBorderRect.x = Mathf.Round(drawRect.xMax - virtualSizes.Right.x * (LocalScale.x * 1.4f));
                rightBorderRect.y = Mathf.Round(drawRect.y + virtualSizes.TopRight.y * (LocalScale.y * 1.4f));
                rightBorderRect.xMax = drawRect.xMax;
                rightBorderRect.yMax = Mathf.Round(drawRect.yMax - virtualSizes.BottomRight.y * (LocalScale.y * 1.4f));

                // Bottom
                bottomBorderRect.x = Mathf.Round(drawRect.x + virtualSizes.BottomLeft.x * (LocalScale.x * 1.4f));
                bottomBorderRect.y = Mathf.Round(drawRect.yMax - virtualSizes.Bottom.y * (LocalScale.y * 1.4f));
                bottomBorderRect.xMax = Mathf.Round(drawRect.xMax - virtualSizes.BottomRight.x * (LocalScale.x * 1.4f));
                bottomBorderRect.yMax = drawRect.yMax;

                // Fill
                fillBordersRect.xMin = Mathf.Round(drawRect.xMin + virtualSizes.Left.x * (LocalScale.x * 1.4f));
                fillBordersRect.yMin = Mathf.Round(drawRect.yMin + virtualSizes.Top.y * (LocalScale.y * 1.4f));
                fillBordersRect.xMax = Mathf.Round(drawRect.xMax - virtualSizes.Right.x * (LocalScale.x * 1.4f));
                fillBordersRect.yMax = Mathf.Round(drawRect.yMax - virtualSizes.Bottom.y * (LocalScale.y * 1.4f));
            }

            #endregion
        }

        #endregion Private Methods

        private static void LoadTextures()
        {
            if (!TextureReplacement.TryImportTexture(TooltipBgTopLeftName, true, out TooltipBgTopLeft))
            {
                return;
            }

            if (!TextureReplacement.TryImportTexture(TooltipBgTopName, true, out TooltipBgTop))
            {
                return;
            }

            if (!TextureReplacement.TryImportTexture(TooltipBgTopRightName, true, out TooltipBgTopRight))
            {
                return;
            }

            if (!TextureReplacement.TryImportTexture(TooltipBgLeftName, true, out TooltipBgLeft))
            {
                return;
            }

            if (!TextureReplacement.TryImportTexture(TooltipBgFillName, true, out TooltipBgFill))
            {
                return;
            }

            if (!TextureReplacement.TryImportTexture(TooltipBgRightName, true, out TooltipBgRight))
            {
                return;
            }

            if (!TextureReplacement.TryImportTexture(TooltipBgBottomLeftName, true, out TooltipBgBottomLeft))
            {
                return;
            }

            if (!TextureReplacement.TryImportTexture(TooltipBgBottomName, true, out TooltipBgBottom))
            {
                return;
            }

            if (!TextureReplacement.TryImportTexture(TooltipBgBottomRightName, true, out TooltipBgBottomRight))
            {
            }
        }

        private bool TryExtractNumber(string str, out int number)
        {
            return int.TryParse(string.Join("", str.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit)), out number);
        }

        #region Localization

        private static void LoadTextData()
        {
            const string csvFilename = "WorldTooltipsModData.csv";

            if (textDataBase == null)
            {
                textDataBase = StringTableCSVParser.LoadDictionary(csvFilename);
            }
        }

        public static string Localize(string key)
        {
            return textDataBase.TryGetValue(key, out var value) ? value : string.Empty;
        }

        #endregion Localization
    }
}