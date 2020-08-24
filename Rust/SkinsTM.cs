using System.Collections.Generic;
using System.Linq;
using System;

using Rust.Workshop;
using UnityEngine;
using Oxide.Core;

using Random = Oxide.Core.Random;

namespace Oxide.Plugins
{
    [Info("SkinsTM", "RaINi_", "1.0.0")]
    [Description("Lets you change the skins of items and entities")]
    class SkinsTM : RustPlugin
    {
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

            LoadSkinsFromCache("SkinsTM");
            
            permission.RegisterPermission("skinstm.use", this);
            permission.RegisterPermission("skinstm.adm", this);

            cmd.AddChatCommand("skin",      this, OnSkinCmd);
            cmd.AddChatCommand("skin_perm", this, OnPermCmd);

            cmd.AddConsoleCommand("skin",      this, nameof(OnSkinCmdConsole));
            cmd.AddConsoleCommand("skin_perm", this, nameof(OnPermCmdConsole));
        }

        /// <summary>
        /// Gets called when our plugin is being unloaded
        /// </summary>
        void Unload()
        {
            SaveSkinsToCache("SkinsTM");
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

            foreach(var skin in Approved.All)
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

        #region Config
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
            if (!player.IPlayer.IsAdmin || !player.IPlayer.HasPermission("skinstm.adm"))
                return;

            if (args.Length < 2)
            {
                player.IPlayer.Reply("Usage: skin_perm <give|remove> <player name/id>");
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
                    if (target.IPlayer.HasPermission("skinstm.use"))
                        return;

                    target.IPlayer.GrantPermission("skinstm.use");
                    target.IPlayer.Reply("You have been granted skin permissions");
                    return;
                }
                case "remove":
                {
                    if (!target.IPlayer.HasPermission("skinstm.use"))
                        return;

                    target.IPlayer.RevokePermission("skinstm.use");
                    target.IPlayer.Reply("Your skin permissions have been revoked");
                    return;
                }
                default:
                    player.IPlayer.Reply("Unknown command");
                    return;
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
            if (!player.IPlayer.HasPermission("skinstm.use"))
                return;

            if (args.Length < 2)
            {
                player.IPlayer.Reply("Usage: skin <self|remote> <random|workshopId>");
                return;
            }

            if (args[0] == "self")
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

                if (args[1] == "random")
                {
                    SetSkin(item, skins.ElementAt(Random.Range(skins.Count())).WorkshopdId);
                }
                else
                {
                    ulong workshopId = 0;

                    if (!ulong.TryParse(args[1], out workshopId) || !skins.Any(x => x.WorkshopdId == workshopId))
                    {
                        player.IPlayer.Reply("Could not parse workshop id or skin does not exist for this item");
                        return;
                    }

                    SetSkin(item, workshopId);
                }
            }
            else
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

                if (args[1] == "random")
                {
                    SetSkin(entity, skins.ElementAt(Random.Range(skins.Count())).WorkshopdId);
                }
                else
                {
                    ulong workshopId = 0;

                    if (!ulong.TryParse(args[1], out workshopId) || !skins.Any(x => x.WorkshopdId == workshopId))
                    {
                        player.IPlayer.Reply("Could not parse workshop id or skin does not exist for this item");
                        return;
                    }

                    SetSkin(entity, workshopId);
                }
            }
        }

        #endregion

        #region Hooks
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
    }
}
