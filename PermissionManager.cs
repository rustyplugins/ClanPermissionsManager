using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using System.Collections.Generic;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("PermissionManager", "73CN0109y", 1.0)]
    [Description("Allows the management of clan member permissions")]
    public class PermissionManager : RustPlugin
    {
        #region Variables
        [PluginReference]
        Plugin Clans;

        public StoredData storedData;
        public static PermissionManager Instance;
        #endregion

        #region Oxide Hooks
        void Init()
        {
            Instance = this;
            storedData = Interface.GetMod().DataFileSystem.ReadObject<StoredData>(Name);

            if(storedData.permissions.Count <= 0)
            {
                storedData.permissions.AddRange(new string[] {
                    "permissions.manage"
                });
            }

            Subscribe("OnClanCreate");
            Subscribe("OnClanUpdate");
            Subscribe("OnClanDestroy");
        }

        private void OnServerShutdown() => SaveData();

        private void Unloaded() => SaveData();

        private void OnNewSave(string s)
        {
            storedData = new StoredData();
            SaveData();
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["DoesNotExist"] = "Permission \"{0}\" doesn't exist!",
                ["NoPermission"] = "You don't have permission to do this!",
                ["EmptyPermissions"] = "You don't have any permissions for the clan you're in!",
                ["ListPermissions"] = "Your Permissions:\n{0}",
                ["NoPlayerFound"] = "Could not find player matching \"{0}\"!",
                ["NotInClan"] = "Permissions is only available to clan members!",
                ["MustBeInClan"] = "You can only give permissions to other players in your clan!",

                ["PermissionGrantedExample"] = "Example: /permissions grant door.use s0meR4ndom3r",
                ["PermissionGranted"] = "You gave {0} the permission \"{1}\"",
                ["PermissionGrantedAll"] = "You gave {0} all permissions",
                ["PermissionGrantedToOther"] = "You now have the permission \"{0}\"",
                ["PermissionGrantedToOtherAll"] = "You now have all permissions",
                ["PermissionGrantError"] = "Could not give {0} the permission \"{1}\"!",
                ["PermissionGrantedErrorAll"] = "Could not give all permissions to \"{1}\"",

                ["PermissionRevokedExample"] = "Example: /permissions revoke door.use s0meR4ndom3r",
                ["PermissionRevoked"] = "{0} no longer has the permission \"{1}\"",
                ["PermissionRevokedAll"] = "{0} no longer has any permissions",
                ["PermissionRevokedToOther"] = "You no longer have the permission \"{0}\"",
                ["PermissionRevokedToOtherAll"] = "You no longer have any permissions",
                ["PermissionRevokedError"] = "Could not revoke the permission \"{0}\" from {1}!",
                ["PermissionRevokedErrorAll"] = "Could not revoke all permissions from \"{1}\"",

                ["HelpError"] = "Permissions are only available to clans!",
                ["Help"] = "Permission Commands:\n{0}\n{1}\n{2}",
                ["HelpList"] = "/permissions - List your permissions",
                ["HelpGrant"] = "/permissions grant [permission] [player name] - Grant a permission to a member in your clan",
                ["HelpRevoke"] = "/permissions revoke [permission] [player name] - Revoke a permission from a member in your clan"
            }, this);
        }

        void OnClanCreate(string clanTag)
        {
            JObject clan = Clans?.Call<JObject>("GetClan", clanTag);
            BasePlayer clanOwner = BasePlayer.Find((string)clan.SelectToken("owner"));

            if(clanOwner == null)
            {
                return;
            }

            if (!storedData.clanPermissions.ContainsKey(clanTag))
            {
                storedData.clanPermissions[clanTag] = new ClanPermissions(clanTag, clanOwner.userID);
            }

            storedData.clanPermissions[clanTag].GivePermission(clanOwner, "permissions.manage");
        }

        void OnClanUpdate(string clanTag)
        {
            JObject clan = Clans.Call<JObject>("GetClan", new object[] { clanTag });
            List<ulong> clanMembers = clan?.SelectToken("members")?.Values<ulong>().ToList();

            if (clanMembers == null) return;

            GetClanPermissions(clanTag)?.Update(clanMembers);
        }

        void OnClanDestroy(string clanTag)
        {
            if (!storedData.clanPermissions.ContainsKey(clanTag)) return;

            storedData.clanPermissions.Remove(clanTag);

            SaveData();
        }
        #endregion

        #region Plugin Hooks
        void RegisterPermissions(params string[] permissions)
        {
            foreach (string permission in permissions)
            {
                if (permission.ToLower().StartsWith("permission") ||
                    storedData.permissions.Contains(permission.ToLower()))
                {
                    continue;
                }

                storedData.permissions.Add(permission.ToLower());
            }

            SaveData();
        }

        bool HasPermission(BasePlayer player, string permission, BaseEntity entity)
        {
            return GetClanPermissions(entity.OwnerID)?.HasPermission(player, permission) ?? false;
        }

        bool GivePermission(BasePlayer ownerPlayer, BasePlayer targetPlayer, string permission)
        {
            return GetClanPermissions(ownerPlayer)?.GivePermission(targetPlayer, permission) ?? false;
        }
        #endregion

        #region Chat Commands
        [ChatCommand("permissions")]
        void cmdPermission(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                ShowPlayerPermissions(player);
                return;
            }

            if (args.Length == 2 && player.IsAdmin)
            {
                if(args[0] != "add")
                {
                    ShowHelp(player);
                    return;
                }

                RegisterPermissions(args.Skip(1).ToArray());
                return;
            }

            if (args.Length < 3)
            {
                ShowHelp(player);
                return;
            }

            string permission = args[1].ToLower();

            if (args.Length >= 3 && permission != "*" && !storedData.permissions.Contains(permission))
            {
                player.ChatMessage(Lang("DoesNotExist", args[1]));
                return;
            }

            bool? canManagePermissions = GetClanPermissions(player)?.HasPermission(player, "permissions.manage");

            if (canManagePermissions != true)
            {
                player.ChatMessage(Lang("NoPermission"));
                return;
            }

            switch (args[0].Trim().ToLower())
            {
                case "grant":
                    GrantPlayerPermissions(player, args);
                    break;
                case "revoke":
                    RevokePlayerPermissions(player, args);
                    break;
            }
        }
        #endregion

        #region Chat Methods
        /**
         * Example;
         * /permissions
         */
        void ShowPlayerPermissions(BasePlayer player)
        {
            ClanPermissions clanPermissions = GetClanPermissions(player);

            if (clanPermissions == null)
            {
                player.ChatMessage(Lang("NotInClan"));
                return;
            }

            List<string> permissions = clanPermissions.GetPermissions(player);

            if (permissions == null || permissions.Count <= 0)
            {
                player.ChatMessage(Lang("EmptyPermissions"));
                return;
            }

            player.ChatMessage(Lang("ListPermissions", string.Join(", ", permissions)));
        }

        /**
         * Example;
         * /permissions grant door.use [test] baz
         * /permissions grant * baz
         */
        void GrantPlayerPermissions(BasePlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                player.ChatMessage(Lang("PermissionGrantedExample"));
                return;
            }

            ClanPermissions clanPermissions = GetClanPermissions(player);

            if (clanPermissions == null)
            {
                ShowHelp(player);
                return;
            }

            string playerName = string.Join(" ", args.Skip(2));
            BasePlayer targetPlayer = BasePlayer.Find(playerName);

            if (targetPlayer == null)
            {
                if (!playerName.StartsWith(clanPermissions.clanTag))
                {
                    targetPlayer = BasePlayer.Find("[" + clanPermissions.clanTag + "] " + playerName);
                }

                if (targetPlayer == null)
                {
                    player.ChatMessage(Lang("NoPlayerFound", playerName));
                    return;
                }
            }

            string targetPlayerClan = Clans?.Call<string>("GetClanOf", new object[] { targetPlayer });

            if (targetPlayerClan != clanPermissions.clanTag)
            {
                player.ChatMessage(Lang("MustBeInClan"));
                return;
            }

            string permission = args[1].ToLower();

            if (clanPermissions.GivePermission(targetPlayer, permission))
            {
                player.ChatMessage(Lang("PermissionGranted" + (permission == "*" ? "All" : ""), targetPlayer.displayName, permission));

                if (targetPlayer.userID != player.userID)
                {
                    targetPlayer.ChatMessage(Lang("PermissionGrantedToOther" + (permission == "*" ? "All" : ""), permission));
                }
            }
            else
            {
                player.ChatMessage(Lang("PermissionGrantedError" + (permission == "*" ? "All" : ""), permission, targetPlayer.displayName));
            }
        }

        /**
         * Example;
         * /permissions revoke door.use [test] baz
         * /permissions revoke * baz
         */
        void RevokePlayerPermissions(BasePlayer player, string[] args)
        {
            if (args.Length < 2)
            {
                player.ChatMessage(Lang("PermissionRevokedExample"));
                return;
            }

            ClanPermissions clanPermissions = GetClanPermissions(player);

            if (clanPermissions == null)
            {
                ShowHelp(player);
                return;
            }

            string playerName = string.Join(" ", args.Skip(2));
            BasePlayer targetPlayer = BasePlayer.Find(playerName);

            if (targetPlayer == null)
            {
                if (!playerName.StartsWith(clanPermissions.clanTag))
                {
                    targetPlayer = BasePlayer.Find("[" + clanPermissions.clanTag + "] " + playerName);
                }

                if (targetPlayer == null)
                {
                    player.ChatMessage(Lang("NoPlayerFound", playerName));
                    return;
                }
            }

            string targetPlayerClan = Clans?.Call<string>("GetClanOf", new object[] { targetPlayer });

            if (targetPlayerClan != clanPermissions.clanTag)
            {
                player.ChatMessage(Lang("MustBeInClan"));
                return;
            }

            string permission = args[1];

            if (clanPermissions.RevokePermission(targetPlayer, permission))
            {
                player.ChatMessage(Lang("PermissionRevoked" + (permission == "*" ? "All" : ""), targetPlayer.displayName, permission));

                if (targetPlayer.userID != player.userID)
                {
                    player.ChatMessage(Lang("PermissionRevokedToOther" + (permission == "*" ? "All" : ""), permission));
                }
            }
            else
            {
                player.ChatMessage(Lang("PermissionRevokedError" + (permission == "*" ? "All" : ""), permission, targetPlayer.displayName));
            }
        }

        void ShowHelp(BasePlayer player)
        {
            string clanTag = Clans?.Call<string>("GetClanOf", new object[] { player });

            if (clanTag == null)
            {
                player.ChatMessage(Lang("HelpError"));
                return;
            }

            player.ChatMessage(Lang("Help", Lang("HelpList"), Lang("HelpGrant"), Lang("HelpRevoke")));
        }
        #endregion

        #region Helpers
        ClanPermissions GetClanPermissions(string clanTag)
        {
            if (clanTag == null)
            {
                return null;
            }

            if (!storedData.clanPermissions.ContainsKey(clanTag))
            {
                OnClanCreate(clanTag);
            }

            return storedData.clanPermissions[clanTag];
        }

        ClanPermissions GetClanPermissions(BasePlayer player)
        {
            string clanTag = Clans?.Call<string>("GetClanOf", new object[] { player });

            return GetClanPermissions(clanTag);
        }

        ClanPermissions GetClanPermissions(ulong ownerID)
        {
            string clanTag = Clans?.Call<string>("GetClanOf", new object[] { ownerID });

            return GetClanPermissions(clanTag);
        }

        JToken GetClan(ulong ownerID) => Clans?.Call<JToken>("GetClan", new object[] { ownerID });
        JToken GetClan(BasePlayer player) => GetClan(player.userID);

        void SaveData() => Interface.GetMod().DataFileSystem.WriteObject(Name, storedData);

        string Lang(string key, params object[] args) => string.Format(lang.GetMessage(key, this), args);
        #endregion

        #region Classes
        public class StoredData
        {
            public List<string> permissions = new List<string>();
            public Dictionary<string, ClanPermissions> clanPermissions = new Dictionary<string, ClanPermissions>();
        }

        public class ClanPermissions
        {
            public string clanTag;
            public ulong ownerID;
            public Dictionary<ulong, List<string>> playerPermissions = new Dictionary<ulong, List<string>>();

            public ClanPermissions(string clanTag, ulong ownerID)
            {
                this.clanTag = clanTag;
                this.ownerID = ownerID;
            }

            public bool HasPermission(BasePlayer player, string permission)
            {
                return (playerPermissions.ContainsKey(player.userID) && playerPermissions[player.userID].Contains(permission.ToLower()));
            }

            private void ToggleAllPermissions(BasePlayer player, bool givePermissions)
            {
                if (playerPermissions.ContainsKey(player.userID) && !givePermissions)
                {
                    playerPermissions[player.userID].RemoveAll(x => x != "permissions.manage");
                }
                else if (givePermissions)
                {
                    if (!playerPermissions.ContainsKey(player.userID))
                    {
                        playerPermissions[player.userID] = new List<string>();
                    }

                    bool canManage = playerPermissions[player.userID].Contains("permissions.manage");

                    playerPermissions[player.userID] = Instance.storedData.permissions.Where(x => canManage || x != "permissions.manage").ToList();
                }

                Instance.SaveData();
            }

            public bool GivePermission(BasePlayer player, string permission)
            {
                if (permission == "*")
                {
                    ToggleAllPermissions(player, true);
                    return true;
                }

                if (HasPermission(player, permission)) return true;

                if (!playerPermissions.ContainsKey(player.userID))
                {
                    playerPermissions[player.userID] = new List<string>();
                }

                playerPermissions[player.userID].Add(permission.ToLower());

                Instance.SaveData();

                return true;
            }

            public bool RevokePermission(BasePlayer player, string permission)
            {
                if (permission == "*")
                {
                    ToggleAllPermissions(player, false);
                    return true;
                }

                if (!HasPermission(player, permission) || !playerPermissions.ContainsKey(player.userID)) return true;

                if(player.userID == ownerID && permission == "permissions.manage")
                {
                    return false;
                }

                playerPermissions[player.userID].Remove(permission.ToLower());

                Instance.SaveData();

                return true;
            }

            public List<string> GetPermissions(BasePlayer player)
            {
                if (!playerPermissions.ContainsKey(player.userID)) return null;

                return playerPermissions[player.userID];
            }

            public void Update(List<ulong> clanMembers)
            {
                List<ulong> removePlayers = new List<ulong>();

                foreach (KeyValuePair<ulong, List<string>> player in playerPermissions)
                {
                    if (!clanMembers.Contains(player.Key))
                    {
                        removePlayers.Add(player.Key);
                    }
                }

                foreach (ulong playerID in removePlayers)
                {
                    playerPermissions.Remove(playerID);
                }

                Instance.SaveData();
            }
        }
        #endregion
    }
}
