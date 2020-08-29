using System.Collections.Generic;
using System.Linq;
using System;

using Rust.Workshop;
using UnityEngine;
using Oxide.Core;

using Random = Oxide.Core.Random;

namespace Oxide.Plugins
{
    [Info("SkinsTM", "RaINi_", "1.1.0")]
    [Description("Lets you change the skins of items and entities")]
    class SkinsTM : RustPlugin
    {
        const string SKIN_CMD           = "skin";
        const string PERM_CMD           = "skin_perm";
        const string SKIN_SETTINGS_CMD  = "skin_setting";

        const string PERM_USE           = "skinstm.use";
        const string PERM_ADM           = "skinstm.adm";

        const string FILE_SKIN_CACHE    = "SkinsTM_Cache";
        const string FILE_USER_SETTINGS = "SkinsTM_UserSettings";

        /// <summary>
        /// Our plugins user settings instance
        /// </summary>
        readonly SettingsData _settings = SettingsData.Load(FILE_USER_SETTINGS);

        /// <summary>
        /// Initializes our plugin
        /// </summary>
        void Init()
        {
            if (Skinnable.All == null)
            {
                // We call this manually cause our plugin got loaded before the
                // game finished loading the assets
                GameManifest.LoadAssets();
            }

            LoadSkinsFromCache(FILE_SKIN_CACHE);

            permission.RegisterPermission(PERM_USE, this);
            permission.RegisterPermission(PERM_ADM, this);

            cmd.AddChatCommand(SKIN_CMD,             this, OnSkinCmd);
            cmd.AddChatCommand(PERM_CMD,             this, OnPermCmd);
            cmd.AddChatCommand(SKIN_SETTINGS_CMD,    this, OnSkinSettingsCmd);

            cmd.AddConsoleCommand(SKIN_CMD,          this, nameof(OnSkinCmdConsole));
            cmd.AddConsoleCommand(PERM_CMD,          this, nameof(OnPermCmdConsole));
            cmd.AddConsoleCommand(SKIN_SETTINGS_CMD, this, nameof(OnSkinSettingsCmdConsole));
        }

        /// <summary>
        /// Gets called when our plugin is being unloaded
        /// </summary>
        void Unload()
        {
            SaveSkinsToCache(FILE_SKIN_CACHE);

            _settings.Save(FILE_USER_SETTINGS);
        }

        /// <summary>
        /// Gets called whenever the server saves
        /// </summary>
        void OnServerSave()
        {
            timer.Once(Random.Range(60), () => _settings.Save(FILE_USER_SETTINGS));
        }

        #region Cache

        class CacheSkin
        {
            public string Name;
            public string ItemName;
            public ulong WorkshopId;
        }

        /// <summary>
        /// Adds skins from a cache file into the internal approved skins list
        /// </summary>
        /// <param name="fileName">The cache file name.</param>
        void LoadSkinsFromCache(string fileName)
        {
            var skinCache = Interface.Oxide.DataFileSystem.ReadObject<List<CacheSkin>>(fileName);
            if (skinCache == null || skinCache.Count == 0)
                return;

            var lastId = Approved.All.Max(x => x.Key);

            foreach (var skin in skinCache)
            {
                if (Approved.All.Any(x => x.Value.WorkshopdId == skin.WorkshopId))
                    continue;

                try
                {
                    AddSkin(new ApprovedSkinInfo(skin.WorkshopId, skin.Name, string.Empty, skin.ItemName)
                        .ItemId(lastId++)
                        .Store(Price.OneDollar, true, true));
                }
                catch (Exception ex)
                {
                    Interface.Oxide.LogError($"Failed to add {skin.Name} to internal approved skins list, {ex}");
                }
            }
        }

        /// <summary>
        /// Filles the skin cache with the skins from the internal approved skins list
        /// </summary>
        /// <param name="fileName">The cache file name.</param>
        void SaveSkinsToCache(string fileName)
        {
            var skinCache = new List<CacheSkin>(Approved.All.Count);

            foreach (var skin in Approved.All)
            {
                skinCache.Add(new CacheSkin
                {
                    Name       = skin.Value.Name,
                    ItemName   = skin.Value.Skinnable.ItemName,
                    WorkshopId = skin.Value.WorkshopdId
                });
            }

            Interface.Oxide.DataFileSystem.WriteObject(fileName, skinCache);
        }

        #endregion

        #region Settings

        class PlayerSettings
        {
            public bool RandomCraftingSkin;
            public Dictionary<string, ulong> Skins = new Dictionary<string, ulong>();
        }

        class SettingsData
        {
            /// <summary>
            /// Our player settings dictionary
            /// </summary>
            public readonly Dictionary<ulong, PlayerSettings> Settings = new Dictionary<ulong, PlayerSettings>();

            /// <summary>
            /// Loads and returns a settings instance
            /// </summary>
            /// <param name="fileName">The settings file name.</param>
            /// <returns></returns>
            public static SettingsData Load(string fileName) => Interface.Oxide.DataFileSystem.ReadObject<SettingsData>(fileName);

            /// <summary>
            /// Saves the current settings instance
            /// </summary>
            /// <param name="fileName">The settings file name.</param>
            public void Save(string fileName) => Interface.Oxide.DataFileSystem.WriteObject(fileName, this);

            /// <summary>
            /// Retrieves setting for a player with the given steam id
            /// </summary>
            /// <param name="id">The players steam id.</param>
            /// <returns></returns>
            public PlayerSettings this[ulong id]
            {
                get
                {
                    PlayerSettings settings;

                    if (!Settings.TryGetValue(id, out settings))
                    {
                        settings = new PlayerSettings
                        {
                            RandomCraftingSkin = false
                        };

                        Settings.Add(id, settings);
                    }

                    return settings;
                }
            }
        }

        #endregion

        #region Commands

        /// <summary>
        /// This method handles all the permission commands for this plugin
        /// </summary>
        /// <param name="player">The player that called the command.</param>
        /// <param name="command">The command.</param>
        /// <param name="args">The command arguments.</param>
        void OnPermCmd(BasePlayer player, string command, string[] args)
        {
            if (!player.IPlayer.IsAdmin || !player.IPlayer.HasPermission(PERM_ADM))
                return;

            if (args.Length < 2)
            {
                player.IPlayer.Reply($"Usage: {PERM_CMD} <give|remove> <playername|id>");
                return;
            }

            ulong steamId;
            ulong.TryParse(args[1], out steamId);

            var target = BasePlayer.allPlayerList.FirstOrDefault(x => x.displayName.Equals(args[1], StringComparison.OrdinalIgnoreCase) || x.userID == steamId);
            if (target == null)
            {
                player.IPlayer.Reply("Could not find player");
                return;
            }

            switch (args[0])
            {
                case "add":
                {
                    if (target.IPlayer.HasPermission(PERM_USE))
                        return;

                    target.IPlayer.GrantPermission(PERM_USE);
                    target.IPlayer.Reply("You have been granted skin permissions");
                    return;
                }
                case "remove":
                {
                    if (!target.IPlayer.HasPermission(PERM_USE))
                        return;

                    target.IPlayer.RevokePermission(PERM_USE);
                    target.IPlayer.Reply("Your skin permissions have been revoked");
                    return;
                }
                default:
                {
                    player.IPlayer.Reply("Unknown command");
                    return;
                }
            }
        }

        /// <summary>
        /// This method handles all the skin command for this plugin
        /// </summary>
        /// <param name="player">The player that called the command.</param>
        /// <param name="command">The command.</param>
        /// <param name="args">The command arguments.</param>
        void OnSkinCmd(BasePlayer player, string command, string[] args)
        {
            if (!player.IPlayer.HasPermission(PERM_USE))
                return;

            if (args.Length < 2)
            {
                player.IPlayer.Reply($"Usage: {SKIN_CMD} <self|remote> <random|workshopId|clear>");
                return;
            }

            switch(args[0])
            {
                case "self":
                {
                    var item = player.GetActiveItem();
                    if (item == null)
                    {
                        player.IPlayer.Reply("You are not holding any item");
                        return;
                    }

                    var skins = GetSkinsForItem(item.info.shortname);
                    if (skins == null)
                    {
                        player.IPlayer.Reply("Could not find any skin for this item");
                        return;
                    }

                    switch (args[1])
                    {
                        case "random":
                        {
                            SetSkin(item, GetRandomElement(skins).WorkshopdId);
                            return;
                        }
                        case "clear":
                        {
                            _settings[player.userID].Skins.Remove(skins.First().Skinnable.ItemName);

                            SetSkin(item, 0);

                            return;
                        }
                        default:
                        {
                            ulong workshopId = 0;

                            if (!ulong.TryParse(args[1], out workshopId) || !skins.Any(x => x.WorkshopdId == workshopId))
                            {
                                player.IPlayer.Reply("Could not parse workshop id or skin does not exist for this item");
                                return;
                            }

                            _settings[player.userID].Skins[skins.First().Skinnable.ItemName] = workshopId;

                            SetSkin(item, workshopId);

                            return;
                        }
                    }
                }
                case "remote":
                {
                    RaycastHit rayHit;

                    if (!Physics.Raycast(player.eyes.HeadRay(), out rayHit, 500f))
                    {
                        player.IPlayer.Reply("Raycast failed, make sure you are looking at a valid item and are within 500 units of it");
                        return;
                    }

                    var entity = rayHit.GetEntity();
                    if (entity == null)
                    {
                        player.IPlayer.Reply("Could not get entity from raycast");
                        return;
                    }

                    if (entity.OwnerID != player.userID)
                    {
                        player.IPlayer.Reply("This entity was not build by you, you can only change the skins of entities you have build");
                        return;
                    }

                    var skins = GetSkinsForItem(entity.ShortPrefabName);
                    if (skins == null)
                    {
                        player.IPlayer.Reply("Could not find any skin for this entity");
                        return;
                    }

                    switch(args[1])
                    {
                        case "random":
                        {
                            SetSkin(entity, GetRandomElement(skins).WorkshopdId);
                            return;
                        }
                        case "clear":
                        {
                            _settings[player.userID].Skins.Remove(skins.First().Skinnable.ItemName);

                            SetSkin(entity, 0);

                            return;
                        }
                        default:
                        {
                            ulong workshopId = 0;

                            if (!ulong.TryParse(args[1], out workshopId) || !skins.Any(x => x.WorkshopdId == workshopId))
                            {
                                player.IPlayer.Reply("Could not parse workshop id or skin does not exist for this item");
                                return;
                            }

                            _settings[player.userID].Skins[skins.First().Skinnable.ItemName] = workshopId;

                            SetSkin(entity, workshopId);

                            return;
                        }
                    }
                }
                default:
                {
                    player.IPlayer.Reply("Unknown command");
                    return;
                }
            }
        }

        /// <summary>
        /// This method handles all the settings commands for our plugin
        /// </summary>
        /// <param name="player">The player that called the command.</param>
        /// <param name="command">The command.</param>
        /// <param name="args">The command arguments.</param>
        void OnSkinSettingsCmd(BasePlayer player, string command, string[] args)
        {
            if (!player.IPlayer.HasPermission(PERM_USE))
                return;

            if (args.Length < 2)
            {
                player.IPlayer.Reply($"Usage: {SKIN_SETTINGS_CMD} <setting> <value>");
                return;
            }

            switch(args[0])
            {
                case "randomcraftingskin":
                case "rcs":
                {
                    if (!bool.TryParse(args[1], out _settings[player.userID].RandomCraftingSkin))
                        player.IPlayer.Reply("Could not parse value");
                    return;
                }
                default:
                {
                    player.IPlayer.Reply("Unknown setting");
                    return;
                }
            }
        }

        #endregion

        #region Hooks

        /// <summary>
        /// Gets called when a player finished crafting an item
        /// </summary>
        /// <param name="task">The item crafting task info.</param>
        /// <param name="item">The crafted item.</param>
        void OnItemCraftFinished(ItemCraftTask task, Item item)
        {
            if (!task.owner.IPlayer.HasPermission(PERM_USE))
                return;

            ulong skinId;

            var userSettings = _settings[task.owner.userID];
            if (userSettings.Skins.TryGetValue(item.info.shortname, out skinId))
            {
                //
            }
            else
            {
                if (!userSettings.RandomCraftingSkin)
                    return;

                var skins = GetSkinsForItem(item.info.shortname);
                if (skins == null)
                    return;

                skinId = GetRandomElement(skins).WorkshopdId;
            }

            SetSkin(item, skinId);
        }

        #endregion

        #region Wrappers

        /// <summary>
        /// Console command wrapper for the skin command
        /// </summary>
        /// <param name="arg">The console command arguments.</param>
        void OnSkinCmdConsole(ConsoleSystem.Arg arg) => OnSkinCmd(arg.Player(), string.Empty, arg.HasArgs() ? arg.Args : new string[] { });

        /// <summary>
        /// Console command wrapper for the permission command
        /// </summary>
        /// <param name="arg">The console command arguments.</param>
        void OnPermCmdConsole(ConsoleSystem.Arg arg) => OnPermCmd(arg.Player(), string.Empty, arg.HasArgs() ? arg.Args : new string[] { });

        /// <summary>
        /// Console command wrapper for the skin command
        /// </summary>
        /// <param name="arg">The console command arguments.</param>
        void OnSkinSettingsCmdConsole(ConsoleSystem.Arg arg) => OnSkinSettingsCmd(arg.Player(), string.Empty, arg.HasArgs() ? arg.Args : new string[] { });

        /// <summary>
        /// Adds a skin to the internal approved skins list
        /// </summary>
        /// <param name="skinInfo"></param>
        void AddSkin(ApprovedSkinInfo skinInfo) => Approved.All.Add(skinInfo.InventoryId, skinInfo);

        /// <summary>
        /// Retrieves all available skins for an item
        /// </summary>
        /// <param name="item">The ItemName or ShortName.</param>
        /// <returns></returns>
        IEnumerable<ApprovedSkinInfo> GetSkinsForItem(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return null;

            if (itemName.EndsWith("deployed"))
            {
                switch(itemName)
                {
                    case "sleepingbag_leather_deployed":
                    {
                        itemName = "sleepingbag";
                        break;
                    }
                    case "vendingmachine.deployed":
                    {
                        itemName = "vending.machine";
                        break;
                    }
                    case "woodbox_deployed":
                    {
                        itemName = "box.wooden";
                        break;
                    }
                    case "reactivetarget_deployed":
                    {
                        itemName = "target.reactive";
                        break;
                    }
                    default:
                        break;
                }
            }

            var skinnable = Skinnable.FindForItem(itemName);
            if (skinnable == null)
                return null;

            return Approved.All
                .Where(x => x.Value.Skinnable == skinnable)
                .Select(x => x.Value);
        }

        /// <summary>
        /// Applies a skin to an item
        /// </summary>
        /// <param name="item">The item to apply the skin on.</param>
        /// <param name="workshopId">The skin workshop id.</param>
        void SetSkin(Item item, ulong workshopId)
        {
            item.skin = workshopId;
            item.MarkDirty();

            var heldEntity = item.GetHeldEntity();
            if (heldEntity == null)
                return;

            SetSkin(heldEntity, workshopId);
        }

        /// <summary>
        /// Applies a skin to a base entity and triggers a network update
        /// </summary>
        /// <param name="entity">The entity to apply the skin to.</param>
        /// <param name="workshopId">The skin workshop id.</param>
        void SetSkin(BaseEntity entity, ulong workshopId)
        {
            entity.skinID = workshopId;
            entity.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Retrieves a random element from an enumerable list
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="items">The items</param>
        /// <returns></returns>
        T GetRandomElement<T>(IEnumerable<T> items)
        {
            if (items == null || !items.Any())
                throw new Exception($"Invalid items list");

            return items.ElementAt(Random.Range(items.Count()));
        }

        #endregion
    }
}
