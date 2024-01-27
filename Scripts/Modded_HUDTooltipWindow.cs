using System;
using System.Linq;
using System.Collections.Generic;

using UnityEngine;

using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.Utility.ModSupport.ModSettings;

using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Utility.AssetInjection;

namespace Modded_Tooltips_Interaction
{
    public class Modded_HUDTooltipWindow : Panel
    {
        #region Singleton

        public static Modded_HUDTooltipWindow Instance { get; private set; }

        #endregion Singleton

        #region Fields

        static Dictionary<string, string> textDataBase = null;

        #region Settings

        const string hideInteractTooltipText = "HideDefaultInteractTooltip";
        public static bool HideInteractTooltip { get; private set; }

        #endregion Settings

        #region Mod API

        const string REGISTER_CUSTOM_TOOLTIP = "RegisterCustomTooltip";
        static Mod mod;

        #endregion Mod API

        #region Raycasting
        //Use the farthest distance
        const float rayDistance = PlayerActivate.StaticNPCActivationDistance;

        private static Dictionary<float, List<Func<RaycastHit, string>>> customGetHoverText = new Dictionary<float, List<Func<RaycastHit, string>>>();

        GameObject mainCamera;
        int playerLayerMask = 0;

        //Caching
        Transform prevHit;
        string prevText;

        GameObject goDoor;
        BoxCollider goDoorCollider;
        StaticDoor prevDoor;
        string prevDoorText;
        float prevDistance;
        Dictionary<int, DoorData> doorDataDict = new Dictionary<int, DoorData>();

        Transform prevDoorCheckTransform;
        Transform prevDoorOwner;
        DaggerfallStaticDoors prevStaticDoors;

        #endregion Raycasting

        PlayerEnterExit playerEnterExit;
        PlayerGPS playerGPS;
        PlayerActivate playerActivate;
        HUDTooltip tooltip;

        #region Stolen methods/variables/properties

        byte[] openHours;
        byte[] closeHours;

        #endregion Stolen methods/variables/properties

        #endregion Fields

        [Invoke(StateManager.StateTypes.Start, 0)]
        public static void Init(InitParams initParams)
        {
            mod = initParams.Mod;
            mod.MessageReceiver = Modded_HUDTooltipWindow.MessageReceiver;

            LoadTextData();

            mod.IsReady = true;

            ModSettings settings = mod.GetSettings();
            HideInteractTooltip = settings.GetValue<bool>("GeneralSettings", hideInteractTooltipText);
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
            // Raycasting
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

            PlayerGPS.OnMapPixelChanged += (_) =>
            {
                Debug.Log("***************Clearing door data cache..");
                doorDataDict.Clear();
            };
        }

        #endregion Constructors

        #region Public Methods

        public override void Draw()
        {
            base.Draw();
            tooltip.Draw();
        }

        public override void Update()
        {
            base.Update();

            // Weird bug occurs when the player is clicking on a static door from a distance because the activation creates another "goDoor"
            // which overlaps and prevents the player from going in. So we must delete the tooltip's goDoor beforehand if the player is activating
            if (InputManager.Instance.ActionComplete(InputManager.Actions.ActivateCenterObject))
            {
                GameObject.Destroy(goDoor);
                goDoor = null;
                goDoorCollider = null;
            }

            tooltip.Scale = DaggerfallUI.Instance.DaggerfallHUD.NativePanel.LocalScale;
            tooltip.AutoSize = AutoSizeModes.Scale;
            AutoSize = AutoSizeModes.None;

            var text = GetHoverText();
            if (!string.IsNullOrEmpty(text))
            {
                tooltip.Draw(text);
            }
        }

        #endregion Public Methods

        #region Custom Modding API

        private static void RegisterCustomTooltip(float activationDistance, Func<RaycastHit, string> func)
        {
            List<Func<RaycastHit, string>> list;
            if (!customGetHoverText.TryGetValue(activationDistance, out list))
            {
                list = customGetHoverText[activationDistance] = new List<Func<RaycastHit, string>>();
            }
            list.Add(func);
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
                return null;

            string result = null;
            bool stop = false;
            foreach (var kv in customGetHoverText)
            {
                if (hit.distance <= kv.Key)
                {
                    foreach (var func in kv.Value)
                    {
                        result = func(hit);

                        if (!string.IsNullOrEmpty(result))
                        {
                            prevDistance = kv.Key;
                            stop = true;
                            break;
                        }
                    }

                    if (stop)
                        break;
                }
            }

            return result;
        }

        private string GetHoverText()
        {
            Ray ray = new Ray(mainCamera.transform.position, mainCamera.transform.forward);

            RaycastHit hit;
            bool hitSomething = Physics.Raycast(ray, out hit, rayDistance, playerLayerMask);

            bool isSame = hit.transform == prevHit;

            if (hitSomething)
            {
                prevHit = hit.transform;

                if (isSame)
                {
                    if (hit.distance <= prevDistance)
                        return prevText;
                    else
                        return null;
                }
                else
                {
                    object comp;
                    string result = null;
                    bool stop = false;

                    result = EnumerateCustomHoverText(hit);

                    // Easy basecases
                    if (string.IsNullOrEmpty(result))
                    {
                        if (hit.transform.name.Length > 16 && hit.transform.name.Substring(0, 17) == "DaggerfallTerrain")
                            stop = true;
                    }

                    if (!stop)
                    {
                        // Objects with "Mobile NPC" activation distances
                        if (string.IsNullOrEmpty(result) && hit.distance <= PlayerActivate.MobileNPCActivationDistance)
                        {
                            if (CheckComponent<MobilePersonNPC>(hit, out comp))
                            {
                                result = ((MobilePersonNPC)comp).NameNPC;
                                prevDistance = PlayerActivate.MobileNPCActivationDistance;
                            }
                            else if (CheckComponent<DaggerfallEntityBehaviour>(hit, out comp))
                            {
                                DaggerfallEntityBehaviour behaviour = (DaggerfallEntityBehaviour)comp;

                                EnemyMotor enemyMotor = behaviour.transform.GetComponent<EnemyMotor>();
                                EnemyEntity enemyEntity = behaviour.Entity as EnemyEntity;

                                if (!enemyMotor || !enemyMotor.IsHostile)
                                {
                                    if (enemyEntity == null)
                                    {
                                        result = behaviour.Entity.Name;
                                    }
                                    else
                                    {
                                        result = TextManager.Instance.GetLocalizedEnemyName(enemyEntity.MobileEnemy.ID);
                                    }
                                    prevDistance = PlayerActivate.MobileNPCActivationDistance;
                                }

                            }
                            else if (CheckComponent<DaggerfallBulletinBoard>(hit, out comp))
                            {
                                result = Localize("BulletinBoard");
                                prevDistance = PlayerActivate.MobileNPCActivationDistance;
                            }
                        }

                        // Objects with "Static NPC" activation distances
                        if (string.IsNullOrEmpty(result) && hit.distance <= PlayerActivate.StaticNPCActivationDistance)
                        {
                            if (CheckComponent<StaticNPC>(hit, out comp))
                            {
                                var npc = (StaticNPC)comp;
                                if (CheckComponent<DaggerfallBillboard>(hit, out comp))
                                {
                                    var bb = (DaggerfallBillboard)comp;
                                    var archive = bb.Summary.Archive;
                                    var index = bb.Summary.Record;

                                    if (archive == 175)
                                    {
                                        switch (index)
                                        {
                                            case 0:
                                                result = TextManager.Instance.GetLocalizedFactionName(index, "Azura"); ;
                                                break;
                                            case 1:
                                                result = TextManager.Instance.GetLocalizedFactionName(index, "Boethiah");
                                                break;
                                            case 2:
                                                result = TextManager.Instance.GetLocalizedFactionName(index, "Clavicus Vile");
                                                break;
                                            case 3:
                                                result = TextManager.Instance.GetLocalizedFactionName(index, "Hircine");
                                                break;
                                            case 4:
                                                result = TextManager.Instance.GetLocalizedFactionName(index, "Hermaeus Mora");
                                                break;
                                            case 5:
                                                result = TextManager.Instance.GetLocalizedFactionName(index, "Malacath");
                                                break;
                                            case 6:
                                                result = TextManager.Instance.GetLocalizedFactionName(index, "Mehrunes Dagon");
                                                break;
                                            case 7:
                                                result = TextManager.Instance.GetLocalizedFactionName(index, "Mephala");
                                                break;
                                            case 8:
                                                result = TextManager.Instance.GetLocalizedFactionName(index, "Meridia");
                                                break;
                                            case 9:
                                                result = TextManager.Instance.GetLocalizedFactionName(index, "Molag Bal");
                                                break;
                                            case 10:
                                                result = TextManager.Instance.GetLocalizedFactionName(index, "Namira");
                                                break;
                                            case 11:
                                                result = TextManager.Instance.GetLocalizedFactionName(index, "Nocturnal");
                                                break;
                                            case 12:
                                                result = TextManager.Instance.GetLocalizedFactionName(index, "Peryite");
                                                break;
                                            case 13:
                                                result = TextManager.Instance.GetLocalizedFactionName(index, "Sanguine");
                                                break;
                                            case 14:
                                                result = TextManager.Instance.GetLocalizedFactionName(index, "Sheogorath");
                                                break;
                                            case 15:
                                                result = TextManager.Instance.GetLocalizedFactionName(index, "Vaermina");
                                                break;
                                        }
                                    }
                                }

                                if (string.IsNullOrEmpty(result))
                                    result = npc.DisplayName;

                                prevDistance = PlayerActivate.StaticNPCActivationDistance;
                            }
                        }

                        // Objects with "Default" activation distances
                        if (hit.distance <= PlayerActivate.DefaultActivationDistance)
                        {
                            if (CheckComponent<DaggerfallAction>(hit, out comp))
                            {
                                var da = (DaggerfallAction)comp;
                                if (da.TriggerFlag == DFBlock.RdbTriggerFlags.Direct
                                    || da.TriggerFlag == DFBlock.RdbTriggerFlags.Direct6
                                    || da.TriggerFlag == DFBlock.RdbTriggerFlags.MultiTrigger)
                                {
                                    bool multiTriggerOkay = false;
                                    var mesh = hit.transform.GetComponent<MeshFilter>();
                                    if (mesh)
                                    {
                                        int record;
                                        if (int.TryParse(string.Join("",
                                            mesh.name
                                                .SkipWhile(c => !char.IsDigit(c))
                                                .TakeWhile(c => char.IsDigit(c)))
                                            , out record))
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
                                    /*else if (CheckComponent<DaggerfallBillboard>(hit, out comp))
                                    {
                                        var bb = ((DaggerfallBillboard)comp);
                                        var archive = bb.Summary.Archive;
                                        var index = bb.Summary.Record;

                                        if (archive == 211)
                                        {
                                            switch (index)
                                            {
                                                case 4:
                                                    ret = "Chain";
                                                    break;
                                            }
                                        }
                                    }*/

                                    if (da.TriggerFlag == DFBlock.RdbTriggerFlags.MultiTrigger && !multiTriggerOkay)
                                    {
                                        result = null;
                                    }
                                    else
                                    {
                                        if (!HideInteractTooltip && string.IsNullOrEmpty(result))
                                            result = Localize("Interact");

                                        prevDistance = PlayerActivate.DefaultActivationDistance;
                                    }

                                }
                            }
                            else if (CheckComponent<DaggerfallLadder>(hit, out comp))
                            {
                                result = Localize("Ladder");
                                prevDistance = PlayerActivate.DefaultActivationDistance;
                            }
                            else if (CheckComponent<DaggerfallBookshelf>(hit, out comp))
                            {
                                result = Localize("Bookshelf");
                                prevDistance = PlayerActivate.DefaultActivationDistance;
                            }
                            else if (CheckComponent<QuestResourceBehaviour>(hit, out comp))
                            {
                                var qrb = (QuestResourceBehaviour)comp;

                                if (qrb.TargetResource != null)
                                {
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
                                            result = DaggerfallUnity.Instance.ItemHelper.ResolveItemLongName(((Item)qrb.TargetResource).DaggerfallUnityItem, false);

                                        prevDistance = PlayerActivate.DefaultActivationDistance;
                                    }
                                }
                            }
                        }

                        // Checking for loot
                        // Corpses have a different activation distance than other containers/loot
                        if (string.IsNullOrEmpty(result) && CheckComponent<DaggerfallLoot>(hit, out comp))
                        {
                            var loot = (DaggerfallLoot)comp;

                            // If a corpse, and within the corpse activation distance..
                            if (loot.ContainerType == LootContainerTypes.CorpseMarker && hit.distance <= PlayerActivate.CorpseActivationDistance)
                            {
                                result = string.Format(Localize("DeadEnemy"), loot.entityName);
                                prevDistance = PlayerActivate.CorpseActivationDistance;
                            }
                            else if (hit.distance <= PlayerActivate.TreasureActivationDistance)
                            {
                                prevDistance = PlayerActivate.TreasureActivationDistance;
                                switch (loot.ContainerType)
                                {
                                    case LootContainerTypes.DroppedLoot:
                                    case LootContainerTypes.RandomTreasure:
                                        if (loot.Items.Count == 1)
                                        {
                                            var item = loot.Items.GetItem(0);
                                            result = item.LongName;

                                            if (item.stackCount > 1)
                                                result = string.Format(Localize("StackCount"), result, item.stackCount);
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
                                            if (int.TryParse(string.Join("",
                                                mesh.name
                                                    .SkipWhile(c => !char.IsDigit(c))
                                                    .TakeWhile(c => char.IsDigit(c)))
                                                , out record))
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
                            }
                        }

                        // Objects with the "Door" activation distances
                        if (string.IsNullOrEmpty(result) && hit.distance <= PlayerActivate.DoorActivationDistance)
                        {
                            if (CheckComponent<DaggerfallActionDoor>(hit, out comp))
                            {
                                var door = (DaggerfallActionDoor)comp;
                                if (!door.IsLocked)
                                    result = Localize("Door");
                                else
                                    result = string.Format(Localize("DoorLockLevel"), door.CurrentLockValue);

                                prevDistance = PlayerActivate.DoorActivationDistance;
                            }
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
                    else
                    {
                        prevHit = null;
                    }

                    prevText = result;

                    return result;
                }
            }

            return null;
        }

        string GetStaticDoorText(DaggerfallStaticDoors doors, RaycastHit hit, Transform doorOwner)
        {
            StaticDoor door;

            //Debug.Log("GETSTATICDOORTEXT"+hashit);
            if (hit.distance <= PlayerActivate.DoorActivationDistance
                && (HasHit(doors, hit.point, out door) || CustomDoor.HasHit(hit, out door)))
            {
                if (door.doorType == DoorTypes.Building && !playerEnterExit.IsPlayerInside)
                {
                    // Check for a static building hit
                    StaticBuilding building;
                    DFLocation.BuildingTypes buildingType;
                    bool buildingUnlocked;
                    int buildingLockValue;

                    Transform buildingOwner;
                    DaggerfallStaticBuildings buildings = GetBuildings(hit.transform, out buildingOwner);
                    if (buildings && buildings.HasHit(hit.point, out building))
                    {
                        // Get building directory for location
                        BuildingDirectory buildingDirectory = GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory();
                        if (!buildingDirectory)
                            return "<ERR: 010>";

                        // Get detailed building data from directory
                        BuildingSummary buildingSummary;
                        if (!buildingDirectory.GetBuildingSummary(building.buildingKey, out buildingSummary))
                            return "<ERR: 011>";

                        buildingUnlocked = playerActivate.BuildingIsUnlocked(buildingSummary);
                        buildingLockValue = playerActivate.GetBuildingLockValue(buildingSummary);
                        buildingType = buildingSummary.BuildingType;

                        // Discover building
                        playerGPS.DiscoverBuilding(building.buildingKey);

                        // Get discovered building
                        PlayerGPS.DiscoveredBuilding db;
                        if (playerGPS.GetDiscoveredBuilding(building.buildingKey, out db))
                        {
                            string tooltip;
                            if (buildingType != DFLocation.BuildingTypes.Town23)
                            {
                                tooltip = string.Format(Localize("GoTo"), db.displayName);
                            }
                            else
                            {
                                tooltip = string.Format(Localize("GoToCityWalls"), playerGPS.CurrentLocalizedLocationName);
                            }

                            if (!buildingUnlocked)
                            {
                                tooltip = string.Format(Localize("LockLevel"), tooltip, buildingLockValue);
                            }

                            if (!buildingUnlocked && buildingType <= DFLocation.BuildingTypes.Palace
                                && buildingType != DFLocation.BuildingTypes.HouseForSale)
                            {
                                string buildingClosedMessage = (buildingType == DFLocation.BuildingTypes.GuildHall)
                                                                ? TextManager.Instance.GetLocalizedText("guildClosed")
                                                                : TextManager.Instance.GetLocalizedText("storeClosed");

                                if (buildingType == DFLocation.BuildingTypes.Palace)
                                    buildingClosedMessage = Localize("PalaceClosed");

                                buildingClosedMessage = buildingClosedMessage.Replace("%d1", openHours[(int)buildingType].ToString());
                                buildingClosedMessage = buildingClosedMessage.Replace("%d2", closeHours[(int)buildingType].ToString());
                                tooltip += "\r" + buildingClosedMessage;
                            }

                            prevDoorText = tooltip;

                            return tooltip;
                        }
                    }

                    //If we caught ourselves hitting the same door again directly without touching the building, just return the previous text which should be the door's
                    return prevDoorText;
                }
                else if (door.doorType == DoorTypes.Building && playerEnterExit.IsPlayerInside)
                {
                    // Hit door while inside, transition outside
                    return string.Format(Localize("GoTo"), playerGPS.CurrentLocalizedLocationName);
                }
                else if (door.doorType == DoorTypes.DungeonEntrance && !playerEnterExit.IsPlayerInside)
                {
                    // Hit dungeon door while outside, transition inside
                    return string.Format(Localize("GoTo"), playerGPS.CurrentLocalizedLocationName);
                }
                else if (door.doorType == DoorTypes.DungeonExit && playerEnterExit.IsPlayerInside)
                {
                    // Hit dungeon exit while inside, ask if access wagon or transition outside
                    if (playerGPS.CurrentLocationType == DFRegion.LocationTypes.TownCity
                        || playerGPS.CurrentLocationType == DFRegion.LocationTypes.TownHamlet
                        || playerGPS.CurrentLocationType == DFRegion.LocationTypes.TownVillage)
                        return string.Format(Localize("GoTo"), playerGPS.CurrentLocalizedLocationName);
                    else
                        return string.Format(Localize("GoToRegion"), playerGPS.CurrentLocalizedRegionName);
                }
            }

            prevHit = null;

            return null;
        }

        class DoorData
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
                Parent = dfuStaticDoors.transform;
                Position = dfuStaticDoors.transform.rotation * door.buildingMatrix.MultiplyPoint3x4(door.centre);
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
        /// </summary>
        /// <param name="point">Hit point from ray test in world space.</param>
        /// <param name="doorOut">StaticDoor out if hit found.</param>
        /// <returns>True if point hits a static door.</returns>
        public bool HasHit(DaggerfallStaticDoors dfuStaticDoors, Vector3 point, out StaticDoor doorOut)
        {
            //Debug.Log("HasHit started");
            doorOut = new StaticDoor();

            if (dfuStaticDoors.Doors == null)
                return false;

            var Doors = dfuStaticDoors.Doors;

            // Using a single hidden trigger created when testing door positions
            // This avoids problems with AABBs as trigger rotates nicely with model transform
            // A trigger is also more useful for debugging as its drawn by editor
            if (goDoor == null)
            {
                goDoor = new GameObject();
                goDoor.hideFlags = HideFlags.HideAndDontSave;
                goDoor.transform.parent = dfuStaticDoors.transform;
                goDoorCollider = goDoor.AddComponent<BoxCollider>();
                goDoorCollider.isTrigger = true;
            }

            BoxCollider c = goDoorCollider;
            bool found = false;

            if (goDoor && prevHit == goDoor.transform && c.bounds.Contains(point))
            {
                //Debug.Log("EARLY FOUND");
                found = true;
                doorOut = prevDoor;
            }

            // Test each door in array

            for (int i = 0; !found && i < Doors.Length; i++)
            {
                int hash = 23;
                unchecked
                {
                    hash = hash * 31 + Doors[i].buildingKey;
                    hash = hash * 31 + Doors[i].blockIndex;
                    hash = hash * 31 + Doors[i].recordIndex;
                    hash = hash * 31 + Doors[i].doorIndex;
                    hash = hash * 31 + i;
                }

                DoorData doorData;
                bool created = false;
                if (!doorDataDict.TryGetValue(hash, out doorData))
                {
                    doorData = doorDataDict[hash] = new DoorData(Doors[i], dfuStaticDoors);
                    created = true;
                }

                //Debug.Log("DOORS ITERATE"+i+", hash:" + hash);

                // Setup single trigger position and size over each door in turn
                // This method plays nice with transforms
                c.size = Doors[i].size;
                goDoor.transform.parent = doorData.Parent;
                goDoor.transform.position = doorData.Position;
                goDoor.transform.rotation = doorData.Rotation;

                // Has to be after setting the parent, position, and rotation of the goDoor
                if (created)
                    doorData.SetMinMax(c.bounds.min, c.bounds.max);

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
                    doorOut = Doors[i];
                    if (doorOut.doorType == DoorTypes.DungeonExit)
                        break;
                }
            }

            // Remove temp trigger
            if (!found && goDoor)
            {
                //Debug.Log("DESTROY");
                GameObject.Destroy(goDoor);
                goDoor = null;
                goDoorCollider = null;
            }
            else if (found)
            {
                prevHit = goDoor.transform;
                prevDoor = doorOut;
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
                    owner = doors.transform;
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
        private DaggerfallStaticBuildings GetBuildings(Transform buildingsTransform, out Transform owner)
        {
            owner = null;
            DaggerfallStaticBuildings buildings = buildingsTransform.GetComponent<DaggerfallStaticBuildings>();
            if (!buildings)
            {
                buildings = buildingsTransform.GetComponentInParent<DaggerfallStaticBuildings>();
                if (buildings)
                    owner = buildings.transform;
            }
            else
            {
                owner = buildings.transform;
            }

            return buildings;
        }

        public class HUDTooltip : BaseScreenComponent
        {
            #region Fields

            const int defaultMarginSize = 2;

            DaggerfallFont font;
            private int currentCursorHeight = -1;
            private int currentSystemHeight;
            private int currentRenderingHeight;
            private bool currentFullScreen;

            bool drawToolTip = false;
            string[] textRows;
            float widestRow = 0;
            string lastText = string.Empty;
            bool previousSDFState;

            #endregion

            #region Properties

            /// <summary>
            /// Gets or sets font used inside tooltip.
            /// </summary>
            public DaggerfallFont Font
            {
                get { return font; }
                set { font = value; }
            }

            /// <summary>
            /// Sets delay time in seconds before tooltip is displayed.
            /// </summary>
            public float ToolTipDelay { get; set; } = 0;

            /// <summary>
            /// Gets or sets tooltip draw position relative to mouse.
            /// </summary>
            public Vector2 MouseOffset { get; set; } = new Vector2(0, 4);

            /// <summary>
            /// Gets or sets tooltip text colour.
            /// </summary>
            public Color TextColor { get; set; } = DaggerfallUI.DaggerfallUnityDefaultToolTipTextColor;

            #endregion

            #region Constructors

            public HUDTooltip()
            {
                font = DaggerfallUI.DefaultFont;
                BackgroundColor = DaggerfallUI.DaggerfallUnityDefaultToolTipBackgroundColor;
                SetMargins(Margins.All, defaultMarginSize);
            }

            #endregion

            #region Public Methods

            public override void Update()
            {
                base.Update();
                if (DaggerfallUnity.Settings.CursorHeight != currentCursorHeight ||
                    Display.main.systemHeight != currentSystemHeight ||
                    Display.main.renderingHeight != currentRenderingHeight ||
                    DaggerfallUnity.Settings.Fullscreen != currentFullScreen)
                    UpdateMouseOffset();
            }

            private void UpdateMouseOffset()
            {
                currentCursorHeight = DaggerfallUnity.Settings.CursorHeight;
                currentSystemHeight = Display.main.systemHeight;
                currentRenderingHeight = Display.main.renderingHeight;
                currentFullScreen = DaggerfallUnity.Settings.Fullscreen;
                MouseOffset = new Vector2(0, 0); //currentCursorHeight * 200f / (currentFullScreen ? currentSystemHeight : currentRenderingHeight));
            }

            /// <summary>
            /// Flags tooltip to be drawn at end of UI update.
            /// </summary>
            /// <param name="text">Text to render inside tooltip.</param>
            public void Draw(string text)
            {
                // Validate
                if (font == null || string.IsNullOrEmpty(text))
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
                    font.GlyphHeight * textRows.Length + TopMargin + BottomMargin - 1);

                // Adjust tooltip position when large HUD is docked to match new viewport size
                if (DaggerfallUI.Instance.DaggerfallHUD != null &&
                    DaggerfallUnity.Settings.LargeHUD &&
                    DaggerfallUnity.Settings.LargeHUDDocked)
                {
                    HUDLarge largeHUD = DaggerfallUI.Instance.DaggerfallHUD.LargeHUD;
                    // Set tooltip position
                    // Don't know why I need to subtract by 2 pixels
                    Position = new Vector2(Screen.width / 2, (Screen.height - largeHUD.Rectangle.height) / 2 - 2);
                }
                else
                {
                    // Set tooltip position without large HUD
                    // Don't know why I need to add one pixel
                    Position = new Vector2(Screen.width / 2, Screen.height / 2 + 1);
                }

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
                    return;

                if (drawToolTip)
                {
                    base.Draw();

                    // Set render area for tooltip to whole screen (material might have been changed by other component, i.e. _ScissorRect might have been set to a subarea of screen (e.g. by TextLabel class))
                    Material material = font.GetMaterial();
                    Vector4 scissorRect = new Vector4(0, 1, 0, 1);
                    material.SetVector("_ScissorRect", scissorRect);

                    // Determine text position
                    Rect rect = Rectangle;
                    Vector2 textPos = new Vector2(
                        rect.x + LeftMargin * Scale.x,
                        rect.y + TopMargin * Scale.y);

                    //if (rect.xMax > Screen.width) textPos.x -= (rect.xMax - Screen.width);

                    // Draw tooltip text
                    for (int i = 0; i < textRows.Length; i++)
                    {
                        font.DrawText(textRows[i], textPos, Scale, TextColor);
                        textPos.y += font.GlyphHeight * Scale.y;
                    }

                    // Lower flag
                    drawToolTip = false;
                }
            }

            #endregion

            #region Private Methods

            void UpdateTextRows(string text)
            {
                // Do nothing if text has not changed since last time
                bool sdfState = font.IsSDFCapable;
                if (text == lastText && sdfState == previousSDFState)
                    return;

                // Split into rows based on \r escape character
                // Text read from plain-text files will become \\r so need to replace this first
                text = text.Replace("\\r", "\r");
                textRows = text.Split('\r');

                // Set text we just processed
                lastText = text;

                // Find widest row
                widestRow = 0;
                for (int i = 0; i < textRows.Length; i++)
                {
                    float width = font.CalculateTextWidth(textRows[i], Scale);
                    if (width > widestRow)
                        widestRow = width;
                }
                previousSDFState = sdfState;
            }

            #endregion
        }

        #endregion Private Methods

        #region Localization
        static void LoadTextData()
        {
            const string csvFilename = "WorldTooltipsModData.csv";

            if (textDataBase == null)
                textDataBase = StringTableCSVParser.LoadDictionary(csvFilename);

            return;
        }

        public static string Localize(string Key)
        {
            if (textDataBase.ContainsKey(Key))
                return textDataBase[Key];

            return string.Empty;
        }
        #endregion Localization
    }
}
