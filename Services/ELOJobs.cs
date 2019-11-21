﻿using Discord;
using Discord.WebSocket;
using RavenBOT.Common;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ELO.Services
{
    public class ELOJobs
    {
        public ELOJobs(DiscordShardedClient client)
        {
            Client = client;
            //Setup a timer that fires every 5 minutes
            CompetitionUpdateTimer = new Timer(RunQueueChecks, null, 60000, 1000 * 60 * 5);
        }

        public DiscordShardedClient Client { get; }

        public Timer CompetitionUpdateTimer { get; }
        public void RunQueueChecks(object stateInfo = null)
        {
            var _ = Task.Run(async () =>
            {
                using (var db = new Database())
                {
                    var now = DateTime.UtcNow;
                    //TODO: Avoid querying ALL queued players
                    var queuedPlayers = db.QueuedPlayers.ToArray();
                    var guildGroups = queuedPlayers.GroupBy(x => x.GuildId);
                    foreach (var group in guildGroups)
                    {
                        var comp = db.GetOrCreateCompetition(group.Key);
                        if (comp.QueueTimeout == null) continue;

                        foreach (var player in group)
                        {
                            //Too much time has passed, user is to be removed from queue.
                            if (player.QueuedAt + comp.QueueTimeout.Value < now)
                            {
                                //Remove player from queue
                                db.QueuedPlayers.Remove(player);
                                //Ensure lobby channel still exists and announce the user is removed from queue
                                var channel = Client.GetChannel(player.ChannelId) as SocketTextChannel;
                                if (channel != null)
                                {
                                    await channel.SendMessageAsync(MentionUtils.MentionUser(player.UserId), false, $"{MentionUtils.MentionUser(player.UserId)} was removed from the queue as they have been queued for more than {comp.QueueTimeout.Value.GetReadableLength()}".QuickEmbed(Color.DarkBlue));
                                }
                            }
                        }
                    }
                    db.SaveChanges();
                }
            });
        }
    }
}
