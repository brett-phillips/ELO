﻿using Discord;
using Discord.Commands;
using Discord.WebSocket;
using ELO.Entities;
using ELO.Models;
using ELO.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ELO.Extensions;
using ELO.Services.Reactive;

namespace ELO.Modules
{
    [RequireContext(ContextType.Guild)]
    public partial class LobbyManagement : ModuleBase<ShardedCommandContext>
    {
        private readonly ReactiveService _reactive;

        public LobbyManagement(Random random, GameService gameService, LobbyService lobbyService, PremiumService premiumService, ReactiveService reactive)
        {
            _reactive = reactive;
            Random = random;
            GameService = gameService;
            LobbyService = lobbyService;
            PremiumService = premiumService;
        }

        //TODO: Player queuing via reactions to a message.
        public Random Random { get; }

        public GameService GameService { get; }

        public LobbyService LobbyService { get; }

        public PremiumService PremiumService { get; }

        [Command("ClearQueue", RunMode = RunMode.Sync)]
        [Alias("Clear Queue", "clearq", "clearque")]
        [Summary("Clears the current queue.")]
        [Preconditions.RequirePermission(PermissionLevel.Moderator)]
        public virtual async Task ClearQueueAsync()
        {
            using (var db = new Database())
            {
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Channel is not a lobby.", Color.Red);
                    return;
                }

                var queuedPlayers = db.QueuedPlayers.AsQueryable().Where(x => x.ChannelId == Context.Channel.Id);
                db.QueuedPlayers.RemoveRange(queuedPlayers);

                var latestGame = db.GameResults.AsQueryable().Where(x => x.LobbyId == Context.Channel.Id).OrderByDescending(x => x.GameId).FirstOrDefault();
                if (latestGame != null && latestGame.GameState == GameState.Picking)
                {
                    latestGame.GameState = GameState.Canceled;
                    db.GameResults.Update(latestGame);

                    //Announce game cancelled.
                    await Context.SimpleEmbedAsync($"Queue Cleared. Game #{latestGame.GameId} was cancelled as a result.", Color.Green);
                }
                else
                {
                    await Context.SimpleEmbedAsync($"Queue Cleared.", Color.Green);
                }

                db.SaveChanges();
            }
        }

        [Command("ForceJoin", RunMode = RunMode.Sync)]
        [Summary("Forcefully adds a user to queue, bypasses minimum points")]
        [Preconditions.RequirePermission(PermissionLevel.Moderator)]
        public virtual async Task ForceJoinAsync(params SocketGuildUser[] users)
        {
            using (var db = new Database())
            {
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Channel is not a lobby.", Color.Red);
                    return;
                }
                var userIds = users.Select(x => x.Id).ToList();
                var userPlayers = db.Players.AsQueryable().Where(x => x.GuildId == Context.Guild.Id && userIds.Contains(x.UserId));
                var queue = db.QueuedPlayers.AsQueryable().Where(x => x.GuildId == Context.Guild.Id && x.ChannelId == Context.Channel.Id).ToList();
                int queueCount = queue.Count;
                var latestGame = db.GameResults.AsQueryable().Where(x => x.LobbyId == Context.Channel.Id).OrderByDescending(x => x.GameId).FirstOrDefault();
                if (latestGame != null && latestGame.GameState == GameState.Picking)
                {
                    await Context.SimpleEmbedAndDeleteAsync("Current game is picking teams, wait until this is completed.", Color.Red);
                    return;
                }

                var added = new List<ulong>();
                foreach (var player in userPlayers)
                {
                    if (queueCount >= lobby.PlayersPerTeam * 2)
                    {
                        //Queue will be reset after teams are completely picked.
                        await Context.SimpleEmbedAsync("Queue is full, wait for teams to be chosen before joining.", Color.DarkBlue);
                        break;
                    }

                    if (queue.Any(x => x.UserId == player.UserId))
                    {
                        await Context.SimpleEmbedAsync($"{MentionUtils.MentionUser(player.UserId)} is already queued.", Color.DarkBlue);
                        continue;
                    }

                    added.Add(player.UserId);
                    db.QueuedPlayers.Add(new QueuedPlayer
                    {
                        UserId = player.UserId,
                        GuildId = Context.Guild.Id,
                        ChannelId = Context.Channel.Id
                    });
                    queueCount++;
                }
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"{string.Join("", added.Select(MentionUtils.MentionUser))} - added to queue.", Color.Green);

                if (queueCount >= lobby.PlayersPerTeam * 2)
                {
                    db.SaveChanges();

                    await LobbyService.LobbyFullAsync(Context, lobby);
                    return;
                }
            }
        }

        [Command("Sub")]
        [Summary("Replace a user in the specified game with another.")]
        [Preconditions.RequirePermission(PermissionLevel.Moderator)]
        public virtual async Task SubUserAsync(SocketGuildUser user, SocketGuildUser replacedWith)
        {
            using (var db = new Database())
            {
                var game = db.GameResults.AsQueryable().Where(x => x.LobbyId == Context.Channel.Id).OrderByDescending(x => x.GameId).FirstOrDefault();
                if (game != null)
                {
                    await SubUserAsync(game.GameId, user, replacedWith);
                }
            }
        }

        [Command("Sub")]
        [Summary("Replace a user in the specified game with another.")]
        [Preconditions.RequirePermission(PermissionLevel.Moderator)]
        public virtual async Task SubUserAsync(int gameNumber, SocketGuildUser user, SocketGuildUser replacedWith)
        {
            using (var db = new Database())
            {
                var lobby = db.GetLobby(Context.Channel);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Current channel is not a lobby.", Color.Red);
                    return;
                }

                var game = db.GameResults.FirstOrDefault(x => x.GameId == gameNumber && x.LobbyId == lobby.ChannelId);
                if (game == null)
                {
                    await Context.SimpleEmbedAsync("Invalid game number.", Color.Red);
                    return;
                }

                if (game.GameState != GameState.Undecided && game.GameState != GameState.Picking)
                {
                    await Context.SimpleEmbedAsync("This command can only be used with undecided games.", Color.Red);
                    return;
                }

                var replaceUser = db.GetUser(replacedWith);
                if (replaceUser == null)
                {
                    await Context.SimpleEmbedAsync($"{replacedWith.Mention} is not registered.");
                    return;
                }

                var team1 = db.GetTeam1(game).ToArray();
                var team2 = db.GetTeam2(game).ToArray();
                var t1c = db.GetTeamCaptain(game, 1);
                var t2c = db.GetTeamCaptain(game, 2);

                // Check if the user being added is already in the game
                if (team1.Any(x => x.UserId == replacedWith.Id) || team2.Any(x => x.UserId == replacedWith.Id) || t1c?.UserId == replacedWith.Id || t2c?.UserId == replacedWith.Id)
                {
                    await Context.SimpleEmbedAsync($"{replacedWith.Mention} is already in this game.");
                    return;
                }

                var captainReplaced = false;
                var queueUserReplaced = false;

                // Check if user is in either team (and fallback to checking queue)
                if (team1.All(x => x.UserId != user.Id) && team2.All(x => x.UserId != user.Id) && t1c?.UserId != user.Id && t2c?.UserId != user.Id)
                {
                    // User is not present in either team and is not a team captain, check if the game is currently picking and replace the member in the queue.
                    if (game.GameState != GameState.Picking)
                    {
                        await Context.SimpleEmbedAsync($"{user.Mention} is not present in the game.");
                        return;
                    }

                    var queuedUsers = db.QueuedPlayers.AsQueryable().Where(x => x.ChannelId == Context.Channel.Id);
                    var current = queuedUsers.SingleOrDefault(x => x.UserId == user.Id);
                    if (current == null && t1c?.UserId != user.Id && t2c?.UserId != user.Id)
                    {
                        // Player is not present in queue team1 or team2 or captain
                        await Context.SimpleEmbedAsync($"{user.Mention} is not present in the game.");
                        return;
                    }

                    // Ensure the user replacing the player is not queued.
                    var replacer = queuedUsers.SingleOrDefault(x => x.UserId == replacedWith.Id);
                    if (replacer != null)
                    {
                        await Context.SimpleEmbedAsync($"{replacedWith.Mention} is in the remaining player pool already.");
                        return;
                    }

                    if (current != null)
                    {
                        db.QueuedPlayers.Remove(current);
                        db.QueuedPlayers.Add(new QueuedPlayer
                        {
                            GuildId = Context.Guild.Id,
                            ChannelId = lobby.ChannelId,
                            UserId = replacedWith.Id
                        });
                        db.SaveChanges();
                        queueUserReplaced = true;
                    }
                }

                // Find player in either team
                var player = team1.SingleOrDefault(x => x.UserId == user.Id);
                if (player == null)
                {
                    player = team2.SingleOrDefault(x => x.UserId == user.Id);
                }

                if (player != null)
                {
                    db.TeamPlayers.Remove(player);
                    db.TeamPlayers.Add(new TeamPlayer
                    {
                        ChannelId = game.LobbyId,
                        GameNumber = game.GameId,
                        UserId = replacedWith.Id,
                        TeamNumber = player.TeamNumber,
                        GuildId = Context.Guild.Id
                    });
                    db.SaveChanges();

                    await Context.SimpleEmbedAsync($"Player {user.Mention} in team **{player.TeamNumber}** was replaced with {replacedWith.Mention}");
                }
                else
                {
                    //Check team captains
                    if (t1c != null && t1c.UserId == user.Id)
                    {
                        t1c.UserId = replacedWith.Id;
                        db.TeamCaptains.Update(t1c);

                        // Game is picking to try to also replace captain in queue if they remain.
                        if (game.GameState == GameState.Picking)
                        {
                            var queuedUsers = db.QueuedPlayers.AsQueryable().Where(x => x.ChannelId == Context.Channel.Id);
                            var current = queuedUsers.SingleOrDefault(x => x.UserId == user.Id);
                            if (current == null && t1c?.UserId != user.Id && t2c?.UserId != user.Id)
                            {
                                // Player is not present in queue team1 or team2 or captain
                                await Context.SimpleEmbedAsync($"{user.Mention} is not present in the game.");
                                return;
                            }

                            // Ensure the user replacing the player is not queued.
                            var replacer = queuedUsers.SingleOrDefault(x => x.UserId == replacedWith.Id);
                            if (replacer != null)
                            {
                                await Context.SimpleEmbedAsync($"{replacedWith.Mention} is in the remaining player pool already.");
                                return;
                            }

                            if (current != null)
                            {
                                db.QueuedPlayers.Remove(current);
                                db.QueuedPlayers.Add(new QueuedPlayer
                                {
                                    GuildId = Context.Guild.Id,
                                    ChannelId = lobby.ChannelId,
                                    UserId = replacedWith.Id
                                });
                                queueUserReplaced = true;
                            }
                        }

                        db.SaveChanges();
                        await Context.SimpleEmbedAsync($"{user.Mention} as a team captain was replaced with {replacedWith.Mention}");
                        captainReplaced = true;
                    }
                    else if (t2c != null && t2c.UserId == user.Id)
                    {
                        t2c.UserId = replacedWith.Id;
                        db.TeamCaptains.Update(t2c);

                        // Game is picking to try to also replace captain in queue if they remain.
                        if (game.GameState == GameState.Picking)
                        {
                            var queuedUsers = db.QueuedPlayers.AsQueryable().Where(x => x.ChannelId == Context.Channel.Id);
                            var current = queuedUsers.SingleOrDefault(x => x.UserId == user.Id);
                            if (current == null && t1c?.UserId != user.Id && t2c?.UserId != user.Id)
                            {
                                // Player is not present in queue team1 or team2 or captain
                                await Context.SimpleEmbedAsync($"{user.Mention} is not present in the game.");
                                return;
                            }

                            // Ensure the user replacing the player is not queued.
                            var replacer = queuedUsers.SingleOrDefault(x => x.UserId == replacedWith.Id);
                            if (replacer != null)
                            {
                                await Context.SimpleEmbedAsync($"{replacedWith.Mention} is in the remaining player pool already.");
                                return;
                            }

                            if (current != null)
                            {
                                db.QueuedPlayers.Remove(current);
                                db.QueuedPlayers.Add(new QueuedPlayer
                                {
                                    GuildId = Context.Guild.Id,
                                    ChannelId = lobby.ChannelId,
                                    UserId = replacedWith.Id
                                });
                                queueUserReplaced = true;
                            }
                        }

                        db.SaveChanges();
                        await Context.SimpleEmbedAsync($"{user.Mention} as a team captain was replaced with {replacedWith.Mention}");
                        captainReplaced = true;
                    }
                }

                // Used to avoid sending two messages for a captain replacement.
                if (queueUserReplaced && !captainReplaced)
                {
                    await Context.SimpleEmbedAsync($"Player {user.Mention} was replaced with {replacedWith.Mention} in remaining player pool.");
                }
            }
        }

        [Command("ForceJoin", RunMode = RunMode.Sync)]
        [Alias("FJ")]
        [Summary("Forcefully adds a user to queue, bypasses minimum points")]
        [Preconditions.RequirePermission(PermissionLevel.Moderator)]
        public virtual async Task ForceJoinAsync(SocketGuildUser user)
        {
            await ForceJoinAsync(new[] { user });
        }

        [Command("ForceRemove", RunMode = RunMode.Sync)]
        [Alias("FR")]
        [Summary("Forcefully removes a player for the queue")]
        [Preconditions.RequirePermission(PermissionLevel.Moderator)]
        public virtual async Task ForceRemoveAsync(SocketGuildUser user)
        {
            using (var db = new Database())
            {
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Channel is not a lobby.", Color.Red);
                    return;
                }

                var latestGame = db.GameResults.AsQueryable().Where(x => x.LobbyId == Context.Channel.Id).OrderByDescending(x => x.GameId).FirstOrDefault();
                if (latestGame != null && latestGame.GameState == GameState.Picking)
                {
                    await Context.SimpleEmbedAsync("You cannot remove a player from a game that is still being picked, try cancelling the game instead.", Color.DarkBlue);
                    return;
                }

                var queuedUser = db.QueuedPlayers.Find(Context.Channel.Id, user.Id);
                if (queuedUser != null)
                {
                    db.QueuedPlayers.Remove(queuedUser);
                    await Context.SimpleEmbedAsync("Player was removed from queue.", Color.DarkBlue);
                    db.SaveChanges();
                }
                else
                {
                    await Context.SimpleEmbedAsync("Player is not in queue and cannot be removed.", Color.DarkBlue);
                    return;
                }
            }
        }

        [Command("Pick", RunMode = RunMode.Sync)]
        [Alias("p")]
        [Summary("Picks the specified player(s) for your team.")]
        public virtual async Task PickPlayersAsync(params SocketGuildUser[] users)
        {
            var ids = users.Select(x => x.Id).ToHashSet();
            if (ids.Count != users.Length)
            {
                await Context.SimpleEmbedAsync("You cannot specify the same user multiple times.");
                return;
            }

            using (var db = new Database())
            {
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Channel is not a lobby.", Color.Red);
                    return;
                }

                var latestGame = db.GameResults.AsQueryable().Where(x => x.LobbyId == Context.Channel.Id).OrderByDescending(x => x.GameId).FirstOrDefault();
                if (latestGame == null)
                {
                    await Context.SimpleEmbedAsync("There is no game to pick for.", Color.DarkBlue);
                    return;
                }

                if (latestGame.GameState != GameState.Picking)
                {
                    await Context.SimpleEmbedAsync("Lobby is currently not picking teams.", Color.DarkBlue);
                    return;
                }

                var queue = db.GetQueuedPlayers(Context.Guild.Id, Context.Channel.Id).ToList();
                var team1 = db.GetTeamPlayers(Context.Guild.Id, Context.Channel.Id, latestGame.GameId, 1).ToArray();
                var team2 = db.GetTeamPlayers(Context.Guild.Id, Context.Channel.Id, latestGame.GameId, 2).ToArray();
                var cap1 = db.GetTeamCaptain(Context.Guild.Id, Context.Channel.Id, latestGame.GameId, 1);
                var cap2 = db.GetTeamCaptain(Context.Guild.Id, Context.Channel.Id, latestGame.GameId, 2);

                if (cap1 != null)
                {
                    var match = queue.FirstOrDefault(x => x.UserId == cap1.UserId);
                    if (match != null)
                    {
                        db.QueuedPlayers.Remove(match);
                        queue.Remove(match);
                    }
                }
                if (cap2 != null)
                {
                    var match = queue.FirstOrDefault(x => x.UserId == cap2.UserId);
                    if (match != null)
                    {
                        db.QueuedPlayers.Remove(match);
                        queue.Remove(match);
                    }
                }

                //Ensure the player is eligible to join a team
                if (users.Any(user => queue.All(x => x.UserId != user.Id)))
                {
                    if (users.Length == 2)
                        await Context.SimpleEmbedAndDeleteAsync("A selected player is not queued for this game.", Color.Red);
                    else
                        await Context.SimpleEmbedAndDeleteAsync("Player is not queued for this game.", Color.Red);
                    return;
                }
                else if (users.Any(u => team1.Any(x => x.UserId == u.Id) || team2.Any(x => x.UserId == u.Id)))
                {
                    if (users.Length == 2)
                        await Context.SimpleEmbedAndDeleteAsync("A selected player is already picked for a team.", Color.Red);
                    else
                        await Context.SimpleEmbedAndDeleteAsync("Player is already picked for a team.", Color.Red);
                    return;
                }
                else if (users.Any(u => u.Id == cap1.UserId || u.Id == cap2.UserId))
                {
                    await Context.SimpleEmbedAndDeleteAsync("You cannot select a captain for picking.", Color.Red);
                    return;
                }

                string pickResponse = null;
                if (latestGame.PickOrder == CaptainPickOrder.PickTwo)
                {
                    var res = await LobbyService.PickTwoAsync(db, Context, lobby, latestGame, users, cap1, cap2);
                    latestGame = res.Item1;
                    pickResponse = res.Item2;
                }
                else if (latestGame.PickOrder == CaptainPickOrder.PickOne)
                {
                    var res = await LobbyService.PickOneAsync(db, Context, latestGame, users, cap1, cap2);
                    latestGame = res.Item1;
                    pickResponse = res.Item2;
                }
                else
                {
                    await Context.SimpleEmbedAsync("There was an error picking your game.", Color.DarkRed);
                    return;
                }

                if (latestGame == null) return;

                db.SaveChanges();
                var allQueued = db.GetTeamFull(latestGame, 1).Union(db.GetTeamFull(latestGame, 2)).ToHashSet();
                latestGame.Picks++;
                db.Update(latestGame);
                var remaining = queue.Where(x => !allQueued.Contains(x.UserId)).ToArray();
                if (remaining.Length == 1)
                {
                    var lastUser = remaining.First();
                    var allMembers = db.TeamPlayers.AsQueryable().Where(x => x.GuildId == Context.Guild.Id && x.ChannelId == Context.Channel.Id && x.GameNumber == latestGame.GameId);

                    //More players in team 1 so add user to team 2
                    if (allMembers.Count(x => x.TeamNumber == 1) > allMembers.Count(x => x.TeamNumber == 2))
                    {
                        db.TeamPlayers.Add(new TeamPlayer
                        {
                            GuildId = Context.Guild.Id,
                            ChannelId = Context.Channel.Id,
                            UserId = lastUser.UserId,
                            GameNumber = latestGame.GameId,
                            TeamNumber = 2
                        });
                    }
                    else
                    {
                        db.TeamPlayers.Add(new TeamPlayer
                        {
                            GuildId = Context.Guild.Id,
                            ChannelId = Context.Channel.Id,
                            UserId = lastUser.UserId,
                            GameNumber = latestGame.GameId,
                            TeamNumber = 1
                        });
                    }
                    allQueued.Add(lastUser.UserId);
                }

                if (allQueued.Count >= lobby.PlayersPerTeam * 2)
                {
                    //Teams have been filled.
                    latestGame.GameState = GameState.Undecided;
                    db.QueuedPlayers.RemoveRange(queue);
                    db.SaveChanges();

                    var res = GameService.GetGameMessage(latestGame, $"Game #{latestGame.GameId} Started",
                            GameFlag.gamestate,
                            GameFlag.lobby,
                            GameFlag.map,
                            GameFlag.usermentions,
                            GameFlag.time);

                    await ReplyAsync(res.Item1, false, res.Item2.Build());

                    if (lobby.GameReadyAnnouncementChannel != null)
                    {
                        var channel = Context.Guild.GetTextChannel(lobby.GameReadyAnnouncementChannel.Value);
                        if (channel != null)
                        {
                            try
                            {
                                await channel.SendMessageAsync(res.Item1, false, res.Item2.Build());
                            }
                            catch
                            {
                                //
                            }
                        }
                    }

                    if (lobby.DmUsersOnGameReady)
                    {
                        await Context.MessageUsersAsync(queue.Select(x => x.UserId).ToArray(), x => MentionUtils.MentionUser(x), res.Item2.Build());
                    }
                }
                else
                {
                    db.SaveChanges();
                    var res = GameService.GetGameMessage(latestGame, "Player(s) picked.",
                            GameFlag.gamestate);
                    res.Item2.AddField("Remaining", string.Join("\n", remaining.Select(x => MentionUtils.MentionUser(x.UserId))));
                    await ReplyAsync(pickResponse ?? "", false, res.Item2.Build());
                }
            }
        }
    }
}