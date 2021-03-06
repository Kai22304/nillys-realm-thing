﻿#region

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using common.data;
using log4net;
using wServer.realm.entities;
using wServer.realm.entities.player;
using wServer.realm.worlds;

#endregion

namespace wServer.realm
{
    public struct RealmTime
    {
        public int thisTickCounts;
        public int thisTickTimes;
        public long tickCount;
        public long tickTimes;
    }

    public class TimeEventArgs : EventArgs
    {
        public TimeEventArgs(RealmTime time)
        {
            Time = time;
        }

        public RealmTime Time { get; private set; }
    }

    public enum PendingPriority
    {
        Emergent,
        Destruction,
        Networking,
        Normal,
        Creation,
    }

    public class RealmManager
    {
        static ILog log = LogManager.GetLogger(typeof(RealmManager));

        public const int MAX_CLIENT = 200;
        public const int MAX_INREALM = 85;

        public static List<string> realmNames = new List<string>
        {
            "Medusa",
            "Beholder",
            "Flayer",
            "Ogre",
            "Cyclops",
            "Sprite",
            "Djinn",
            "Slime",
            "Blob",
            "Demon",
            "Spider",
            "Scorpion",
            "Ghost"
        };

        public static List<string> allRealmNames = new List<string>
        {
            "Medusa",
            "Beholder",
            "Flayer",
            "Ogre",
            "Cyclops",
            "Sprite",
            "Djinn",
            "Slime",
            "Blob",
            "Demon",
            "Spider",
            "Scorpion",
            "Ghost"
        };

        public static List<string> battleArenaName = new List<string>
        {
            "Battle Arena Portal"
        };

        public static int nextWorldId = 0;
        public static int nextTestId = 0;
        public static readonly ConcurrentDictionary<int, World> Worlds = new ConcurrentDictionary<int, World>();
        public static readonly ConcurrentDictionary<int, Vault> Vaults = new ConcurrentDictionary<int, Vault>();
        public static readonly Dictionary<string, GuildHall> GuildHalls = new Dictionary<string, GuildHall>();

        public static readonly ConcurrentDictionary<int, ClientProcessor> Clients =
            new ConcurrentDictionary<int, ClientProcessor>();

        public static ConcurrentDictionary<int, World> PlayerWorldMapping = new ConcurrentDictionary<int, World>();

        public static ConcurrentDictionary<string, World> ShopWorlds = new ConcurrentDictionary<string, World>();
        private static Thread network;
        private static Thread logic;

        static RealmManager()
        {
            /*log.Info("Initializing Realm Manager...");
            Worlds[World.TUT_ID] = new Tutorial(true);
            Worlds[World.NEXUS_ID] = Worlds[0] = new Nexus();
            Worlds[World.NEXUS_LIMBO] = new NexusLimbo();
            Worlds[World.VAULT_ID] = new Vault(true);
            //Worlds[World.TEST_ID] = new Test();
            Worlds[World.RAND_REALM] = new RandomRealm();


            Monitor = new RealmPortalMonitor(Worlds[World.NEXUS_ID] as Nexus);

            AddWorld(GameWorld.AutoName(1, true));

            /*MerchantLists.GetKeys();
            MerchantLists.AddPetShop();
            MerchantLists.AddCustomShops();
            foreach (var i in MerchantLists.shopLists)
            {
                ShopWorlds.TryAdd(i.Key, AddWorld(new ShopMap(i.Key)));
            }

            log.Info("Realm Manager initialized.");*/
        }

        public void Initialize()
        {
            log.Info("Initializing Realm Manager...");
            AddWorld(World.NEXUS_ID, Worlds[0] = new Nexus());
            Monitor = new RealmPortalMonitor(Worlds[World.NEXUS_ID] as Nexus);

            AddWorld(World.TUT_ID, new Tutorial(true));
            AddWorld(World.NEXUS_LIMBO, new NexusLimbo());
            AddWorld(World.VAULT_ID, new Vault(true));
            AddWorld(World.TEST_ID, new Test());
            AddWorld(World.RAND_REALM, new RandomRealm());

            AddWorld(GameWorld.AutoName(1, true));

            log.Info("Realm Manager initialized.");
        }

        public static RealmPortalMonitor Monitor { get; private set; }
        public static NetworkTicker Network { get; private set; }
        public static LogicTicker Logic { get; private set; }

        public XmlDatas GameData { get; private set; }
        public ChatManager Chat { get; private set; }

        public static bool TryConnect(ClientProcessor psr)
        {
            Account acc = psr.Account;
            if (psr.IP.Banned)
                return false;
            if (acc.Banned)
                return false;
            if (Clients.Count >= MAX_CLIENT)
                return false;
            return Clients.TryAdd(psr.Account.AccountId, psr);
        }

        public static void Disconnect(ClientProcessor psr)
        {
            if (psr == null) // happens sometimes, not sure why
            {
                log.Info("RealmManager.Disconnect() -> psr = null");
                return;
            }
            
            psr.Save();
            psr.Stage = ProtocalStage.Disconnected; 
            // network ticker will remove client so the following line isn't needed
            //Clients.TryRemove(psr.Account.AccountId, out psr);
        }

        public static Vault PlayerVault(ClientProcessor processor)
        {
            Vault v;
            int id = processor.Account.AccountId;
            if (Vaults.ContainsKey(id))
            {
                v = Vaults[id];
            }
            else
            {
                v = Vaults[id] = (Vault) AddWorld(new Vault(false, processor));
            }
            return v;
        }

        public static World GuildHallWorld(string g)
        {
            if (!GuildHalls.ContainsKey(g))
            {
                var gh = (GuildHall) AddWorld(new GuildHall(g));
                GuildHalls.Add(g, gh);
                return GuildHalls[g];
            }
            if (GuildHalls[g].Players.Count == 0)
            {
                GuildHalls.Remove(g);
                var gh = (GuildHall) AddWorld(new GuildHall(g));
                GuildHalls.Add(g, gh);
            }
            return GuildHalls[g];
        }

        public bool RemoveWorld(World world)
        {
            if (world.Manager == null)
                throw new InvalidOperationException("World is not added.");
            if (world == null)
                throw new InvalidOperationException("World is not added.");
            if (Worlds.TryRemove(world.Id, out world))
            {
                try
                {
                    OnWorldRemoved(world);
                    //world.Dispose();
                    GC.Collect();
                }
                catch (Exception e)
                { }
                return true;
            }
            return false;
        }

        private void OnWorldRemoved(World world)
        {
            world.Manager = null;
            if (world is GameWorld)
                Monitor.WorldRemoved(world);
            log.InfoFormat("{1} ({0}) removed.", world.Id, world.Name);
        }

        public static void CloseWorld(World world)
        {
            Monitor.WorldRemoved(world);
        }

        public static World AddWorld(int id, World world)
        {
            if (world.Manager != null)
                throw new InvalidOperationException("World already added.");
            world.Id = id;
            Worlds[id] = world;
            OnWorldAdded(world);
            return world;
        }
        public static World AddWorld(World world)
        {
            if (world.Manager != null)
                throw new InvalidOperationException("World already added.");
            AddWorld(Interlocked.Increment(ref nextWorldId), world);
            return world;
        }
        static void OnWorldAdded(World world)
        {
            //world.Manager = this;
            if (world is GameWorld)
                Monitor.WorldAdded(world);
            log.InfoFormat("{1} ({0}) added.", world.Id, world.Name);
        }

        public static World GetWorld(int id)
        {
            World ret;
            if (!Worlds.TryGetValue(id, out ret)) return null;
            if (ret.Id == 0) return null;
            return ret;
        }

        public static List<Player> GuildMembersOf(string guild)
        {
            return (from i in Worlds
                where i.Key != 0
                from e in i.Value.Players
                where String.Equals(e.Value.Guild, guild, StringComparison.CurrentCultureIgnoreCase)
                select e.Value).ToList();
        }

        public static Player FindPlayer(string name)
        {
            if (name.Split(' ').Length > 1)
                name = name.Split(' ')[1];
            return (from i in Worlds
                where i.Key != 0
                from e in i.Value.Players
                where String.Equals(e.Value.Client.Account.Name, name, StringComparison.CurrentCultureIgnoreCase)
                select e.Value).FirstOrDefault();
        }

        public static Player FindPlayer(int accountId)
        {
            if (Clients.ContainsKey(accountId))
            {
                return Clients[accountId].Player;
            }
            return null;
        }

        public static Player FindPlayerRough(string name)
        {
            Player dummy;
            foreach (var i in Worlds)
                if (i.Key != 0)
                    if ((dummy = i.Value.GetUniqueNamedPlayerRough(name)) != null)
                        return dummy;
            return null;
        }

        //public CommandManager Commands { get; private set; }

        public void Run()
        {
            log.Info("Starting Realm Manager...");

            Network = new NetworkTicker();
            Logic = new LogicTicker();
            network = new Thread(Network.TickLoop)
            {
                Name = "Network",
                CurrentCulture = CultureInfo.InvariantCulture
            };
            logic = new Thread(Logic.TickLoop)
            {
                Name = "Logic",
                CurrentCulture = CultureInfo.InvariantCulture
            };
            //Start logic loop first
            logic.Start();
            network.Start();

            log.Info("Realm Manager started.");
        }
    }
}