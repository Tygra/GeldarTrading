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
        internal static string filepath { get { return Path.Combine(TShock.SavePath, "GeldarTrading.json"); } }
        public override string Author { get { return "Originally IcyPhoenix. Updated, customised: Tygra"; } }
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
                new SqlColumn("ItemID", MySqlDbType.Text),
                new SqlColumn("Stack", MySqlDbType.Int32),
                new SqlColumn("Moneyamount", MySqlDbType.Int32),
                new SqlColumn("Active", MySqlDbType.Int32)
                ));
            sqlcreator.EnsureTableStructure(new SqlTable("moneyqueue",
                new SqlColumn("ID", MySqlDbType.Int32) { Primary = true, AutoIncrement = true },
                new SqlColumn("Receiver", MySqlDbType.VarChar) { Length = 30 },
                new SqlColumn("Sender", MySqlDbType.VarBinary) { Length = 30 },
                new SqlColumn("TradeID", MySqlDbType.Text),
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
                    for (int i = 0; i < 50; i++)
                    {
                        if (args.TPlayer.inventory[i].netID == item.netID)
                        {
                            database.Query("INSERT INTO trade(Username, ItemID, Stack, Moneyamount, Active) VALUES(@0, @1, @2, @3, @4);", args.Player.Name, item.netID, stack, money, 1);
                            if (args.TPlayer.inventory[i].stack == stack)
                            {
                                args.TPlayer.inventory[i].SetDefaults(0);
                            }
                            else
                            {
                                args.TPlayer.inventory[i].stack -= stack;
                            }
                            NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, Terraria.Localization.NetworkText.Empty, args.Player.Index, i);
                            args.Player.SendInfoMessage("{0} of {1} added for {2}.", stack, args.Parameters[1], money);
                            return;
                        }
                    }
                }
            }
        }
    }
}
