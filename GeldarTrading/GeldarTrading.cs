#region Disclaimer
/*  
 *  The plugin has some features that I got from other authors.
 *  I don't claim any ownership over those elements which were made by someone else.
 *  The plugin has been customized to fit our need at Geldar,
 *  and because of this, it's useless for anyone else.
 *  I know timers are shit, and If someone knows a way to keep them after relog, tell me.
*/
#endregion

#region Refs
using System;
using System.Data;
using System.IO;
using System.IO.Streams;
using System.ComponentModel;
using System.Timers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;

//Terraria related refs
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;
using TShockAPI.DB;
using TShockAPI.Localization;
using Newtonsoft.Json;
using Mono.Data.Sqlite;
using MySql.Data.MySqlClient;
using Wolfje.Plugins.SEconomy;
using Wolfje.Plugins.SEconomy.Journal;
#endregion

namespace GeldarTrading
{
    [ApiVersion(2, 1)]
    public class GeldarTrading : TerrariaPlugin
    {
        #region Info & Stuff
        public IDbConnection database;
        public String SavePath = TShock.SavePath;
        public GTPlayer[] Playerlist = new GTPlayer[256];
        internal static string filepath { get { return Path.Combine(TShock.SavePath, "GeldarTrading.json"); } }
        public override string Author { get { return "Tygra"; } }
        public override string Description { get { return "Seconomy based Shop"; } }
        public override string Name { get { return "Geldar Trading"; } }
        public override Version Version { get { return new Version(1, 0); } }

        public GeldarTrading(Main game)
            : base(game)
        {

        }
        #endregion

        #region Initialize
        public override void Initialize()
        {
            Commands.ChatCommands.Add(new Command("geldar.admin", TradeConfig.Reloadcfg, "tradereload"));
            Commands.ChatCommands.Add(new Command("geldar.trade", Trade, "trade"));
            ServerApi.Hooks.GameInitialize.Register(this, OnInitialize);
            ServerApi.Hooks.ServerJoin.Register(this, OnJoin);
            ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
            if (!TradeConfig.ReadConfig())
            {
                TShock.Log.ConsoleError("Config loading failed. Consider deleting it.");
            }
        }
        #endregion

        #region Dispose
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.GameInitialize.Deregister(this, OnInitialize);
                ServerApi.Hooks.ServerJoin.Deregister(this, OnJoin);
                ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
            }
            base.Dispose(disposing);
        }
        #endregion

        #region OnInitialize
        private void OnInitialize(EventArgs args)
        {
            switch (TShock.Config.StorageType.ToLower())
            {
                case "mysql":
                    string[] host = TShock.Config.MySqlHost.Split(':');
                    database = new MySqlConnection()
                    {
                        ConnectionString = string.Format("Server={0}; Port={1}; Database={2}; Uid={3}; Pwd={4}",
                        host[0],
                        host.Length == 1 ? "3306" : host[1],
                        TShock.Config.MySqlDbName,
                        TShock.Config.MySqlUsername,
                        TShock.Config.MySqlPassword)
                    };
                    break;

                case "sqlite":
                    string sql = Path.Combine(TShock.SavePath, "trade.sqlite");
                    database = new SqliteConnection(string.Format("uri=file://{0},Version=3", sql));
                    break;
            }

            SqlTableCreator sqlcreator = new SqlTableCreator(database, database.GetSqlType() == SqlType.Sqlite ? (IQueryBuilder)new SqliteQueryCreator() : new MysqlQueryCreator());
            sqlcreator.EnsureTableStructure(new SqlTable("trade",
                new SqlColumn("ID", MySqlDbType.Int32) { Primary = true, AutoIncrement = true },
                new SqlColumn("Username", MySqlDbType.VarChar) { Length = 30 },
                new SqlColumn("ItemID", MySqlDbType.Int32),
                new SqlColumn("Itemname", MySqlDbType.Text),
                new SqlColumn("Stack", MySqlDbType.Int32),
                new SqlColumn("Moneyamount", MySqlDbType.Int32),
                new SqlColumn("Active", MySqlDbType.Int32)
                ));
            sqlcreator.EnsureTableStructure(new SqlTable("moneyqueue",
                new SqlColumn("ID", MySqlDbType.Int32) { Primary = true, AutoIncrement = true },
                new SqlColumn("Receiver", MySqlDbType.VarChar) { Length = 30 },
                new SqlColumn("Sender", MySqlDbType.VarChar) { Length = 30 },
                new SqlColumn("TradeID", MySqlDbType.Int32),
                new SqlColumn("Moneyamount", MySqlDbType.Int32),
                new SqlColumn("Active", MySqlDbType.Int32)
                ));
        }
        #endregion

        #region getItem
        private Item getItem(TSPlayer player, string itemNameOrId, int stack)
        {
            Item item = new Item();
            List<Item> matchedItems = TShock.Utils.GetItemByIdOrName(itemNameOrId);
            if (matchedItems == null || matchedItems.Count == 0)
            {
                player.SendErrorMessage("Error: Incorrect item name or ID, please use quotes if the item has a space in it!");
                player.SendErrorMessage("Error: You have entered: {0}", itemNameOrId);
                return null;
            }
            else if (matchedItems.Count > 1)
            {
                TShock.Utils.SendMultipleMatchError(player, matchedItems.Select(i => i.Name));
                return null;
            }
            else
            {
                item = matchedItems[0];
            }
            if (stack > item.maxStack)
            {
                player.SendErrorMessage("Error: Stacks entered is greater then maximum stack size");
                return null;
            }
            return item;
        }
        #endregion

        #region Playerlist Join/Leave
        public void OnJoin(JoinEventArgs args)
        {
            Playerlist[args.Who] = new GTPlayer(args.Who);
        }

        public void OnLeave(LeaveEventArgs args)
        {
            Playerlist[args.Who] = null;
        }
        #endregion

        #region Trade command
        private void Trade(CommandArgs args)
        {
            if (args.Parameters.Count < 1)
            {
                args.Player.SendInfoMessage("Double qoutes around item names are required.");
                args.Player.SendInfoMessage("Available commands /trade add/search/accep/cancel/list/collect/check.");
                return;
            }

            #region Trade add
            if (args.Parameters.Count > 0 && args.Parameters[0].ToLower() == "add")
            {
                if (args.Parameters.Count == 1)
                {
                    args.Player.SendInfoMessage("Info: Double qoutes around item names are required.");
                    args.Player.SendInfoMessage("Important: Traded item will lose its prefix.");
                    args.Player.SendInfoMessage("Info:    /trade add \"item name\" amount moneyamount");
                    args.Player.SendInfoMessage("Example: /trade add \"Cactus Sword\" 1 100");
                    args.Player.SendInfoMessage("Info: There is a trade add cost which grows by level.");
                    //trade add costs missing
                    return;
                }

                if (args.Parameters.Count == 4)
                {
                    var Journalpayment = BankAccountTransferOptions.AnnounceToSender;
                    var selectedPlayer = SEconomyPlugin.Instance.GetBankAccount(args.Player.Name);
                    var playeramount = selectedPlayer.Balance;
                    var player = Playerlist[args.Player.Index];
                    QueryResult reader;
                    List<string> activequests = new List<string>();
                    reader = database.QueryReader("SELECT * FROM trade WHERE Username=@0 AND Active=@1;", args.Player.Name, 1);
                    if (reader.Read())
                    {
                        activequests.Add(reader.Get<string>("Username"));
                    }
                    if (activequests.Count < 5)
                    {
                        int stack;
                        if (!int.TryParse(args.Parameters[2], out stack))
                        {
                            args.Player.SendErrorMessage("Invalid stack size.");
                            return;
                        }
                        if (stack <= 0)
                        {
                            args.Player.SendErrorMessage("Stack size can't be zero or less.");
                            return;
                        }
                        int money;
                        if (!int.TryParse(args.Parameters[3], out money))
                        {
                            args.Player.SendErrorMessage("Invalid moneyamount.");
                            return;
                        }
                        if (money <= 0)
                        {
                            args.Player.SendErrorMessage("Moneyamount can't be zero or less.");
                            return;
                        }
                        Item item = getItem(args.Player, args.Parameters[1], stack);
                        if (item == null)
                        {
                            return;
                        }
                        TSPlayer ply = args.Player;
                        for (int i = 0; i < 50; i++)
                        {
                            if (ply.TPlayer.inventory[i].netID == item.netID)
                            {
                                if (ply.TPlayer.inventory[i].stack >= stack)
                                {
                                    database.Query("INSERT INTO trade(Username, ItemID, Itemname, Stack, Moneyamount, Active) VALUES(@0, @1, @2, @3, @4, @5);", args.Player.Name, item.netID, item.Name, stack, money, 1);
                                    if (ply.TPlayer.inventory[i].stack == stack)
                                    {
                                        ply.TPlayer.inventory[i].SetDefaults(0);
                                    }
                                    else
                                    {
                                        ply.TPlayer.inventory[i].stack -= stack;
                                    }
                                    if (args.Player.Group.HasPermission("geldar.level.5"))
                                    {
                                        Money moneyamount = -TradeConfig.contents.level5addcost;
                                        Money moneyamount2 = TradeConfig.contents.level5addcost;
                                        if (playeramount >= moneyamount2)
                                        {
                                            NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, NetworkText.Empty, ply.Index, i);
                                            NetMessage.SendData((int)PacketTypes.PlayerSlot, ply.Index, -1, NetworkText.Empty, ply.Index, i);
                                            SEconomyPlugin.Instance.WorldAccount.TransferToAsync(selectedPlayer, moneyamount, Journalpayment, string.Format("You paid {0} for adding a trade.", moneyamount2, args.Player.Name), string.Format("Trade add"));
                                            args.Player.SendInfoMessage("{0} of {1} added for {2} and paid {3} for adding a trade.", stack, args.Parameters[1], money, moneyamount2);
                                            return;
                                        }
                                        else
                                        {
                                            args.Player.SendErrorMessage("You need {0} to add trades. You have {1}.", moneyamount2, selectedPlayer.Balance);
                                            return;
                                        }
                                    }
                                    else if (args.Player.Group.HasPermission("geldar.level.10"))
                                    {
                                        Money moneyamount = -TradeConfig.contents.level10addcost;
                                        Money moneyamount2 = TradeConfig.contents.level10addcost;
                                        if (playeramount >= moneyamount2)
                                        {
                                            NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, NetworkText.Empty, ply.Index, i);
                                            NetMessage.SendData((int)PacketTypes.PlayerSlot, ply.Index, -1, NetworkText.Empty, ply.Index, i);
                                            SEconomyPlugin.Instance.WorldAccount.TransferToAsync(selectedPlayer, moneyamount, Journalpayment, string.Format("You paid {0} for adding a trade.", moneyamount2, args.Player.Name), string.Format("Trade add"));
                                            args.Player.SendInfoMessage("{0} of {1} added for {2} and paid {3} for adding a trade.", stack, args.Parameters[1], money, moneyamount2);
                                            return;
                                        }
                                        else
                                        {
                                            args.Player.SendErrorMessage("You need {0} to add trades. You have {1}.", moneyamount2, selectedPlayer.Balance);
                                            return;
                                        }
                                    }
                                    else if (args.Player.Group.HasPermission("geldar.level.20"))
                                    {
                                        Money moneyamount = -TradeConfig.contents.level20addcost;
                                        Money moneyamount2 = TradeConfig.contents.level20addcost;
                                        if (playeramount >= moneyamount2)
                                        {
                                            NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, NetworkText.Empty, ply.Index, i);
                                            NetMessage.SendData((int)PacketTypes.PlayerSlot, ply.Index, -1, NetworkText.Empty, ply.Index, i);
                                            SEconomyPlugin.Instance.WorldAccount.TransferToAsync(selectedPlayer, moneyamount, Journalpayment, string.Format("You paid {0} for adding a trade.", moneyamount2, args.Player.Name), string.Format("Trade add"));
                                            args.Player.SendInfoMessage("{0} of {1} added for {2} and paid {3} for adding a trade.", stack, args.Parameters[1], money, moneyamount2);
                                            return;
                                        }
                                        else
                                        {
                                            args.Player.SendErrorMessage("You need {0} to add trades. You have {1}.", moneyamount2, selectedPlayer.Balance);
                                            return;
                                        }
                                    }
                                    else if (args.Player.Group.HasPermission("geldar.level.30"))
                                    {
                                        Money moneyamount = -TradeConfig.contents.level30addcost;
                                        Money moneyamount2 = TradeConfig.contents.level30addcost;
                                        if (playeramount >= moneyamount2)
                                        {
                                            NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, NetworkText.Empty, ply.Index, i);
                                            NetMessage.SendData((int)PacketTypes.PlayerSlot, ply.Index, -1, NetworkText.Empty, ply.Index, i);
                                            SEconomyPlugin.Instance.WorldAccount.TransferToAsync(selectedPlayer, moneyamount, Journalpayment, string.Format("You paid {0} for adding a trade.", moneyamount2, args.Player.Name), string.Format("Trade add"));
                                            args.Player.SendInfoMessage("{0} of {1} added for {2} and paid {3} for adding a trade.", stack, args.Parameters[1], money, moneyamount2);
                                            return;
                                        }
                                        else
                                        {
                                            args.Player.SendErrorMessage("You need {0} to add trades. You have {1}.", moneyamount2, selectedPlayer.Balance);
                                            return;
                                        }
                                    }
                                    else if (args.Player.Group.HasPermission("geldar.level.40"))
                                    {
                                        Money moneyamount = -TradeConfig.contents.level40addcost;
                                        Money moneyamount2 = TradeConfig.contents.level40addcost;
                                        if (playeramount >= moneyamount2)
                                        {
                                            NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, NetworkText.Empty, ply.Index, i);
                                            NetMessage.SendData((int)PacketTypes.PlayerSlot, ply.Index, -1, NetworkText.Empty, ply.Index, i);
                                            SEconomyPlugin.Instance.WorldAccount.TransferToAsync(selectedPlayer, moneyamount, Journalpayment, string.Format("You paid {0} for adding a trade.", moneyamount2, args.Player.Name), string.Format("Trade add"));
                                            args.Player.SendInfoMessage("{0} of {1} added for {2} and paid {3} for adding a trade.", stack, args.Parameters[1], money, moneyamount2);
                                            return;
                                        }
                                        else
                                        {
                                            args.Player.SendErrorMessage("You need {0} to add trades. You have {1}.", moneyamount2, selectedPlayer.Balance);
                                            return;
                                        }
                                    }
                                    else if (args.Player.Group.HasPermission("geldar.level.50"))
                                    {
                                        Money moneyamount = -TradeConfig.contents.level50addcost;
                                        Money moneyamount2 = TradeConfig.contents.level50addcost;
                                        if (playeramount >= moneyamount2)
                                        {
                                            NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, NetworkText.Empty, ply.Index, i);
                                            NetMessage.SendData((int)PacketTypes.PlayerSlot, ply.Index, -1, NetworkText.Empty, ply.Index, i);
                                            SEconomyPlugin.Instance.WorldAccount.TransferToAsync(selectedPlayer, moneyamount, Journalpayment, string.Format("You paid {0} for adding a trade.", moneyamount2, args.Player.Name), string.Format("Trade add"));
                                            args.Player.SendInfoMessage("{0} of {1} added for {2} and paid {3} for adding a trade.", stack, args.Parameters[1], money, moneyamount2);
                                            return;
                                        }
                                        else
                                        {
                                            args.Player.SendErrorMessage("You need {0} to add trades. You have {1}.", moneyamount2, selectedPlayer.Balance);
                                            return;
                                        }
                                    }
                                    else if (args.Player.Group.HasPermission("geldar.level.60"))
                                    {
                                        Money moneyamount = -TradeConfig.contents.level60addcost;
                                        Money moneyamount2 = TradeConfig.contents.level60addcost;
                                        if (playeramount >= moneyamount2)
                                        {
                                            NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, NetworkText.Empty, ply.Index, i);
                                            NetMessage.SendData((int)PacketTypes.PlayerSlot, ply.Index, -1, NetworkText.Empty, ply.Index, i);
                                            SEconomyPlugin.Instance.WorldAccount.TransferToAsync(selectedPlayer, moneyamount, Journalpayment, string.Format("You paid {0} for adding a trade.", moneyamount2, args.Player.Name), string.Format("Trade add"));
                                            args.Player.SendInfoMessage("{0} of {1} added for {2} and paid {3} for adding a trade.", stack, args.Parameters[1], money, moneyamount2);
                                            return;
                                        }
                                        else
                                        {
                                            args.Player.SendErrorMessage("You need {0} to add trades. You have {1}.", moneyamount2, selectedPlayer.Balance);
                                            return;
                                        }
                                    }
                                    else if (args.Player.Group.HasPermission("geldar.level.70") || args.Player.Group.HasPermission("geldar.level.80") || args.Player.Group.HasPermission("geldar.level.90") || args.Player.Group.HasPermission("geldar.level.100"))
                                    {
                                        Money moneyamount = -TradeConfig.contents.maxaddcost;
                                        Money moneyamount2 = TradeConfig.contents.maxaddcost;
                                        if (playeramount >= moneyamount2)
                                        {
                                            NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, NetworkText.Empty, ply.Index, i);
                                            NetMessage.SendData((int)PacketTypes.PlayerSlot, ply.Index, -1, NetworkText.Empty, ply.Index, i);
                                            SEconomyPlugin.Instance.WorldAccount.TransferToAsync(selectedPlayer, moneyamount, Journalpayment, string.Format("You paid {0} for adding a trade.", moneyamount2, args.Player.Name), string.Format("Trade add"));
                                            args.Player.SendInfoMessage("{0} of {1} added for {2} and paid {3} for adding a trade.", stack, args.Parameters[1], money, moneyamount2);
                                            return;
                                        }
                                        else
                                        {
                                            args.Player.SendErrorMessage("You need {0} to add trades. You have {1}.", moneyamount2, selectedPlayer.Balance);
                                            return;
                                        }
                                    }                                    
                                    else
                                    {
                                        args.Player.SendErrorMessage("You don't have permission to add trades.");
                                        return;
                                    }
                                }
                            }
                        }
                        args.Player.SendErrorMessage("you don't have the item or you don't have enough of it.");
                        args.Player.SendErrorMessage("Item name provided: {0}. Stack: {1}.", item.Name, stack);
                        return;
                    }
                    else
                    {
                        args.Player.SendErrorMessage("You have the maximum active trades.");
                        args.Player.SendErrorMessage("Maximum active trade for your rank: {0}.", TradeConfig.contents.maxactivetrades);
                        return;
                    }
                }
                else
                {
                    args.Player.SendErrorMessage("Invalid syntax. Use /trade add to get syntax information");
                    return;
                }
            }
            #endregion

            #region Trade search
            if (args.Parameters.Count > 0 && args.Parameters[0].ToLower() == "search")
            {
                if (args.Parameters.Count == 1)
                {
                    args.Player.SendInfoMessage("Info: Double qoutes around item names are required.");
                    args.Player.SendInfoMessage("With search, you can get the trade ID.");
                    args.Player.SendInfoMessage("Info:    /trade search \"item name\"");
                    args.Player.SendInfoMessage("Example: /trade search \"Cactus Sword\"");
                    return;
                }

                if (args.Parameters.Count > 1)
                {
                    int pageNumber;
                    int pageParamIndex = 2;
                    if (!PaginationTools.TryParsePageNumber(args.Parameters, pageParamIndex, args.Player, out pageNumber))
                        return;

                    string itemname = string.Join(" ", args.Parameters[1]);
                    List<string> result = new List<string>();
                    using (var reader = database.QueryReader("SELECT * FROM trade WHERE Itemname=@0 AND Active=@1;", itemname, 1))
                    {
                        bool read = false;
                        while (reader.Read())
                        {
                            read = true;
                            result.Add(String.Format("{0}" + " - " + "{1}" + " - " + "{2}" + " - " + "{3} TC", reader.Get<int>("ID"), reader.Get<string>("Itemname"), reader.Get<int>("Stack"), reader.Get<int>("Moneyamount")));
                        }
                        if (!read)
                        {
                            args.Player.SendErrorMessage("No items found by that name.");
                            args.Player.SendErrorMessage("Item name provided: {0}.", itemname);
                            return;
                        }
                    }
                    PaginationTools.SendPage(args.Player, pageNumber, result,
                    new PaginationTools.Settings
                    {
                        MaxLinesPerPage = 10,
                        HeaderFormat = "ID - Itemname - Stack - Cost ({0}/{1})",
                        FooterFormat = "Type {0}trade search {1} {{0}} for more.".SFormat(Commands.Specifier, itemname),
                        NothingToDisplayString = "No items found by that name"
                    });
                }
            }
            #endregion

            #region Accept
            if (args.Parameters.Count > 0 && args.Parameters[0].ToLower() == "accept")
            {
                if (args.Parameters.Count == 2)
                {
                    var Journalpayment = BankAccountTransferOptions.AnnounceToSender;
                    var selectedPlayer = SEconomyPlugin.Instance.GetBankAccount(args.Player.User.Name);
                    var playeramount = selectedPlayer.Balance;
                    var player = Playerlist[args.Player.Index];
                    string param2 = string.Join(" ", args.Parameters[1]);
                    List<string> username = new List<string>();
                    List<string> itemName = new List<string>();
                    List<int> cost = new List<int>();
                    List<int> itemid = new List<int>();
                    List<int> amount = new List<int>();
                    var id = Convert.ToInt32(param2);
                    if (id <= 0)
                    {
                        args.Player.SendErrorMessage("ID can't be zero or less.");
                        return;
                    }
                    if (args.Player.InventorySlotAvailable)
                    {
                        using (var reader = database.QueryReader("SELECT * FROM trade WHERE ID=@0 AND Active=@1;", id, 1))
                        {
                            bool read = false;
                            while (reader.Read())
                            {
                                read = true;
                                username.Add(reader.Get<string>("Username"));
                                itemName.Add(reader.Get<string>("Itemname"));
                                cost.Add(reader.Get<int>("Moneyamount"));
                                itemid.Add(reader.Get<int>("ItemID"));
                                amount.Add(reader.Get<int>("Stack"));
                            }
                            if (!read)
                            {
                                args.Player.SendErrorMessage("ID is not valid or someone else bought it. ID provided {0}.", id);
                                return;
                            }
                        }
                        string receiver = username.FirstOrDefault();
                        string itemname = itemName.FirstOrDefault();
                        int money = cost.FirstOrDefault();
                        int item = itemid.FirstOrDefault();
                        int stack = amount.FirstOrDefault();
                        if (receiver != args.Player.Name)
                        {
                            if (playeramount >= money)
                            {
                                database.Query("UPDATE trade SET Active=@0 WHERE ID=@1;", 0, id);
                                database.Query("INSERT INTO moneyqueue(Receiver, Sender, TradeID, Moneyamount, Active) VALUES(@0, @1, @2, @3, @4);", receiver, args.Player.Name, id, money, 1);
                                SEconomyPlugin.Instance.WorldAccount.TransferToAsync(selectedPlayer, money, Journalpayment, string.Format("You paid {0} for {1} of {2}.", money, stack, itemname, args.Player.Name), string.Format("Trade accept. TC: {0}. Item: {1}", money, itemname));
                                Item itemById = TShock.Utils.GetItemById(item);
                                args.Player.GiveItem(itemById.type, itemById.Name, itemById.width, itemById.height, stack, 0);
                                args.Player.SendInfoMessage("You paid {0} for {1} {2}.", money, stack, itemname);
                            }
                            else
                            {
                                args.Player.SendErrorMessage("You don't have enough Terra Coins to buy this item.");
                                args.Player.SendErrorMessage("You need : {0} TC. You have: {1} TC.", money, selectedPlayer.Balance);
                                return;
                            }
                        }
                        else
                        {
                            args.Player.SendErrorMessage("You can't buy your own trades.");
                            return;
                        }
                    }
                    else
                    {
                        args.Player.SendErrorMessage("Your inventory seems to be full. Free up one slot, and try again.");
                        return;
                    }
                }
                else
                {
                    args.Player.SendErrorMessage("Invalid syntax. Use /trade accept ID.");
                    args.Player.SendErrorMessage("Get the ID from /trade search \"item name\". Double qoutes are required.");
                    return;
                }
            }
            #endregion

            #region Cancel
            if (args.Parameters.Count > 0 && args.Parameters[0].ToLower() == "cancel")
            {
                if (args.Parameters.Count == 2)
                {
                    string param2 = string.Join(" ", args.Parameters[1]);
                    var id = Convert.ToInt32(param2);
                    List<string> selfcheck = new List<string>();
                    List<int> item = new List<int>();
                    List<int> amount = new List<int>();
                    List<string> itemname = new List<string>();
                    if (id <= 0)
                    {
                        args.Player.SendErrorMessage("ID can't be zero or less.");
                        return;
                    }
                    if (args.Player.InventorySlotAvailable)
                    {
                        using (var reader = database.QueryReader("SELECT * FROM trade WHERE ID=@0 AND Active=@1;", id, 1))
                        {
                            bool read = false;
                            while (reader.Read())
                            {
                                read = true;
                                selfcheck.Add(reader.Get<string>("Username"));
                                item.Add(reader.Get<int>("ItemID"));
                                amount.Add(reader.Get<int>("Stack"));
                                itemname.Add(reader.Get<string>("Itemname"));
                            }
                            if (!read)
                            {
                                args.Player.SendErrorMessage("ID is not valid. ID provided {0}.", id);
                                return;
                            }
                        }
                        string username = selfcheck.FirstOrDefault();
                        int itemid = item.FirstOrDefault();
                        int stack = amount.FirstOrDefault();
                        string iname = itemname.FirstOrDefault();
                        if (username == args.Player.Name)
                        {
                            database.Query("UPDATE trade SET Active=@0 WHERE ID=@1;", 0, id);
                            Item itemById = TShock.Utils.GetItemById(itemid);
                            args.Player.GiveItem(itemById.type, itemById.Name, itemById.width, itemById.height, stack, 0);
                            args.Player.SendInfoMessage("Your trade with the ID: {0} has been canceled. You got {1} {2}(s) back.", id, stack, iname);
                        }
                        else
                        {
                            args.Player.SendErrorMessage("This is not your trade. ID provided: {0}.", id);
                            return;
                        }
                    }
                    else
                    {
                        args.Player.SendErrorMessage("Your inventory seems to be full. Free up one slot, and try again.");
                        return;
                    }
                }
                else
                {
                    args.Player.SendErrorMessage("Invalid syntax. Use /trade cancel ID.");
                    args.Player.SendErrorMessage("Get the ID from /trade check.");
                    return;
                }
            }
            #endregion

            #region Trade list
            if (args.Parameters.Count > 0 && args.Parameters[0].ToLower() == "list")
            {
                int pageNumber;
                int pageParamIndex = 2;
                if (!PaginationTools.TryParsePageNumber(args.Parameters, pageParamIndex, args.Player, out pageNumber))
                    return;

                List<string> tradelist = new List<string>();
                using (var reader = database.QueryReader("SELECT * FROM trade WHERE Active=@0;", 1))
                {
                    bool read = false;
                    while (reader.Read())
                    {
                        tradelist.Add(String.Format("{0}" + " - " + "{1}" + " - " + "{2}" + " - " + "{3} TC", reader.Get<int>("ID"), reader.Get<string>("Itemname"), reader.Get<int>("Stack"), reader.Get<int>("Moneyamount")));
                        read = true;
                    }
                    if (!read)
                    {
                        args.Player.SendErrorMessage("The trade list is empty.");
                        return;
                    }
                    PaginationTools.SendPage(args.Player, pageNumber, tradelist,
                    new PaginationTools.Settings
                    {
                        MaxLinesPerPage = 15,
                        HeaderFormat = "ID - Itemname - Stack - Cost ({0}/{1})",
                        FooterFormat = "Type {0}trade list {{0}} for more.".SFormat(Commands.Specifier)
                    });
                }
            }
            #endregion

            #region Trade collect
            if (args.Parameters.Count > 0 && args.Parameters[0].ToLower() == "collect")
            {
                var player = Playerlist[args.Player.Index];
                IBankAccount Player = SEconomyPlugin.Instance.GetBankAccount(player.Index);
                List<int> money = new List<int>();
                using (var reader = database.QueryReader("SELECT * FROM moneyqueue WHERE Receiver=@0 AND Active=@1;", args.Player.Name, 1))
                {
                    bool read = false;
                    while (reader.Read())
                    {
                        money.Add(reader.Get<int>("Moneyamount"));
                        read = true;
                    }
                    if (!read)
                    {
                        args.Player.SendErrorMessage("You don't have anything to collect.");
                        return;
                    }
                    int moneyamount = money.Sum();
                    double percentage = (moneyamount * 0.9);
                    int transferamount = Convert.ToInt32(percentage);
                    database.Query("UPDATE moneyqueue SET Active=@0 WHERE Receiver=@1 AND Active=@2;", 0, args.Player.Name, 1);
                    SEconomyPlugin.Instance.WorldAccount.TransferToAsync(Player, transferamount, BankAccountTransferOptions.AnnounceToReceiver, "Trade collect.", "Trade collect.");
                    args.Player.SendInfoMessage("You have collected {0} Terra Coins for your finished trades.", transferamount);
                }
            }
            #endregion

            #region Trade check
            if (args.Parameters.Count > 0 && args.Parameters[0].ToLower() == "check")
            {
                int pageNumber;
                if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                {
                    return;
                }
                List<string> check = new List<string>();
                using (var reader = database.QueryReader("SELECT * FROM trade WHERE Username=@0 AND Active=@1;", args.Player.Name, 1))
                {
                    bool read = false;
                    while (reader.Read())
                    {
                        check.Add(String.Format("{0}" + " - " + "{1}" + " - " + "{2}" + " - " + "{3} TC", reader.Get<int>("ID"), reader.Get<string>("Itemname"), reader.Get<int>("Stack"), reader.Get<int>("Moneyamount")));
                        read = true;
                    }
                    if (!read)
                    {
                        args.Player.SendErrorMessage("You don't have any active trades.");
                        return;
                    }
                }
                PaginationTools.SendPage(args.Player, pageNumber, check,
                    new PaginationTools.Settings
                    {
                        MaxLinesPerPage = 5,
                        HeaderFormat = "ID - Itemname - Stack - Cost ({0}/{1})",
                        FooterFormat = "Type {0}trade check {{0}} for more.".SFormat(Commands.Specifier)
                    });
            }
            #endregion            
        }
        #endregion
    }
}
