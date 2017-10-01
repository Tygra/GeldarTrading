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
        public IDbConnection database;
        public String SavePath = TShock.SavePath;
        public GTPlayer[] Playerlist = new GTPlayer[256];
        internal static string filepath { get { return Path.Combine(TShock.SavePath, "GeldarTrading.json"); } }
        public override string Author { get { return "Tygra"; } }
        public override string Description { get { return "Seconomy based Shop"; } }
        public override string Name { get { return "Geldar Trading"; } }
        public override Version Version { get { return new Version(1, 0); } }

        public GeldarTrading(Main game)
            :base(game)
        {

        }

        public override void Initialize()
        {
            Commands.ChatCommands.Add(new Command(TradeConfig.Reloadcfg, "tradereload"));
            Commands.ChatCommands.Add(new Command(Trade, "trade"));
            ServerApi.Hooks.GameInitialize.Register(this, OnInitialize);
            ServerApi.Hooks.ServerJoin.Register(this, OnJoin);
            ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
            if (!TradeConfig.ReadConfig())
            {
                TShock.Log.ConsoleError("Config loading failed. Consider deleting it.");
            }
        }

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

        private void Trade(CommandArgs args)
        {
            if (args.Parameters.Count > 0 && args.Parameters[0].ToLower() == "add")
            {
                if (args.Parameters.Count == 4)
                {
                    int stack;
                    if (!int.TryParse(args.Parameters[2], out stack))
                    {
                        args.Player.SendErrorMessage("Invalid stack size");
                        return;
                    }
                    if (stack <= 0)
                    {
                        args.Player.SendErrorMessage("zero or lower");
                        return;
                    }
                    int money;
                    if (!int.TryParse(args.Parameters[3], out money))
                    {
                        args.Player.SendErrorMessage("Invalidy money");
                        return;
                    }
                    if (money <= 0)
                    {
                        args.Player.SendErrorMessage("mone zero or lower");
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
                                NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, Terraria.Localization.NetworkText.Empty, ply.Index, i);
                                NetMessage.SendData((int)PacketTypes.PlayerSlot, ply.Index, -1, Terraria.Localization.NetworkText.Empty, ply.Index, i);
                                args.Player.SendInfoMessage("{0} of {1} added for {2}.", stack, args.Parameters[1], money);
                                return;
                            }
                        }
                    }
                    args.Player.SendErrorMessage("no item or not enough");
                    args.Player.SendErrorMessage("item name: {0}", item.Name);
                    return;
                }
            }

            if (args.Parameters.Count > 0 && args.Parameters[0].ToLower() == "search")
            {
                if (args.Parameters.Count == 3)
                {
                    int pageNumber;
                    if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                    {
                        return;
                    }
                    QueryResult reader;
                    string itemname = string.Join(" ", args.Parameters[2]);
                    List<string> result = new List<string>();
                    reader = database.QueryReader("SELECT * FROM trade WHERE Itemname=@0 AND Active=@1;", itemname, 1);
                    if (reader.Read())
                    {
                        result.Add(String.Format("{0}" + " - " + "{1}" + " - " + "{2}" + " - " + "{3}", reader.Get<int>("ID"), reader.Get<string>("Itemname"), reader.Get<int>("Stack"), reader.Get<int>("Moneyamount")));
                    }
                    else
                    {
                        args.Player.SendErrorMessage("no items found");
                        return;
                    }
                    PaginationTools.SendPage(args.Player, pageNumber, result,
                    new PaginationTools.Settings
                    {
                        MaxLinesPerPage = TradeConfig.contents.maxsearchlinesperpage,
                        HeaderFormat = "ID - Itemname - Stack - Cost ({0}/{1})",
                        FooterFormat = "Type {0}trade search {{0}} for more.".SFormat(Commands.Specifier)
                    });

                }
            }

            if (args.Parameters.Count > 0 && args.Parameters[0].ToLower() == "accept")
            {
                if (args.Parameters.Count == 2)
                {
                    //inventory check
                    var Journalpayment = BankAccountTransferOptions.AnnounceToSender;
                    var selectedPlayer = SEconomyPlugin.Instance.GetBankAccount(args.Player.User.Name);
                    var playeramoun = selectedPlayer.Balance;
                    var player = Playerlist[args.Player.Index];
                    string param2 = string.Join(" ", args.Parameters[2]);
                    QueryResult reader;
                    List<string> seller = new List<string>();
                    List<int> cost = new List<int>();
                    int id = Convert.ToInt32(param2);
                    if (id <= 0)
                    {
                        args.Player.SendErrorMessage("zero or lower");
                        return;
                    }
                    reader = database.QueryReader("SELECT * FROM trade WHERE ID=@;", id);
                    if (reader.Read())
                    {
                        seller.Add(reader.Get<string>("Username"));
                        cost.Add(reader.Get<int>("Moneyamount"));
                    }
                    var receiver = seller.ElementAt(0);
                    int money = cost.ElementAt(0);
                    database.Query("UPDATE trade SET Active=@0 WHERE ID=@1;", 0, id);
                    database.Query("INSERT INTO moneyqueue(Receiver, Sender, TradeID, Moneyamount, Active) VALUES(@0, @1, @2, @3, @4);", receiver, args.Player.Name, id, money, 1);
                    //give item and successmessage
                }
            }

            if (args.Parameters.Count > 0 && args.Parameters[0].ToLower() == "cancel")
            {

            }

            if (args.Parameters.Count > 0 && args.Parameters[0].ToLower() == "list")
            {

            }

            if (args.Parameters.Count > 0 && args.Parameters[0].ToLower() == "collect")
            {

            }

            if (args.Parameters.Count > 0 && args.Parameters[0].ToLower() == "check")
            {

            }
        }
    }
}
