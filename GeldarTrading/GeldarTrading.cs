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
        internal ShopData ShopList;
        internal TradeData Tradelist;
        public IDbConnection database;
        public String SavePath = TShock.SavePath;
        public Shopconfig configobj { get; set; }
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
            ServerApi.Hooks.GameInitialize.Register(this, OnInitialize);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ServerApi.Hooks.GameInitialize.Deregister(this, OnInitialize);
                database.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
