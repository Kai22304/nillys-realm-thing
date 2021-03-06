﻿#region

using System.Collections.Generic;
using System.Linq;
using wServer.realm;
using wServer.realm.entities;
using wServer.realm.worlds;
using wServer.svrPackets;

#endregion

namespace wServer.logic.cond
{
    internal class PentaractStar : Behavior
    {
        protected override bool TickCore(RealmTime time)
        {
            Entity[] entities = GetNearestEntities(28, 0x0d5e)
                .Concat(GetNearestEntities(28, 0x0d60))
                .ToArray();
            if (entities.Length != 5)
                return true;

            var packets = new List<Packet>();
            World owner = Host.Self.Owner;
            if (!entities.Any(_ => _.ObjectType == 0x0d5e))
            {
                var players = new HashSet<Entity>();
                foreach (var i in entities.SelectMany(_ => (_ as Enemy).DamageCounter.GetPlayerData()))
                    if (i.Item1.Quest == Host)
                        players.Add(i.Item1);
                foreach (Entity i in players)
                    packets.Add(new NotificationPacket
                    {
                        ObjectId = i.Id,
                        Color = new ARGB(0xFF00FF00),
                        Text = "Quest Complete!"
                    });

                if (Host.Self.Owner is GameWorld)
                    (Host.Self.Owner as GameWorld).EnemyKilled(Host as Enemy,
                        (entities.Last() as Enemy).DamageCounter.Parent.LastHitter);
                Despawn.Instance.Tick(Host, time);
                foreach (Entity i in entities)
                    Die.Instance.Tick(i, time);
            }
            else
            {
                bool hasCorpse = entities.Any(_ => _.ObjectType == 0x0d60);
                for (int i = 0; i < entities.Length; i++)
                    for (int j = i + 1; j < entities.Length; j++)
                    {
                        packets.Add(new ShowEffectPacket
                        {
                            TargetId = entities[i].Id,
                            EffectType = EffectType.Stream,
                            Color = new ARGB(hasCorpse ? 0xffffff00 : 0xffff0000),
                            PosA = new Position
                            {
                                X = entities[j].X,
                                Y = entities[j].Y
                            },
                            PosB = new Position
                            {
                                X = entities[i].X,
                                Y = entities[i].Y
                            }
                        });
                    }
            }
            owner.BroadcastPackets(packets, null);

            return true;
        }
    }
}