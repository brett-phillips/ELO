﻿using Discord;
using Discord.Commands;
using Discord.WebSocket;
using ELO.Entities;
using ELO.Models;
using ELO.Services;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ELO.Extensions;

namespace ELO.Modules
{
    [RequireContext(ContextType.Guild)]
    [Preconditions.RequirePermission(PermissionLevel.ELOAdmin)]
    public class LobbySetup : ModuleBase<ShardedCommandContext>
    {
        public PremiumService Premium { get; }

        public LobbySetup(PremiumService premium)
        {
            Premium = premium;
        }

        [Command("CreateLobby", RunMode = RunMode.Sync)]
        [Alias("Create Lobby")]
        [Summary("Creates a lobby with the specified players per team and specified pick mode")]
        public virtual async Task CreateLobbyAsync(int playersPerTeam = 5, PickMode pickMode = PickMode.Captains_RandomHighestRanked)
        {
            using (var db = new Database())
            {
                //Required for foreign key check
                var competition = db.GetOrCreateCompetition(Context.Guild.Id);
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby != null)
                {
                    await Context.SimpleEmbedAndDeleteAsync("This channel is already a lobby. Remove the lobby before trying top create a new one here.", Color.Red);
                    return;
                }
                var allLobbies = db.Lobbies.AsQueryable().Where(x => x.GuildId == Context.Guild.Id).ToArray();
                if (allLobbies.Length >= Premium.PremiumConfig.LobbyLimit)
                {
                    if (!Premium.IsPremium(Context.Guild.Id))
                    {
                        await Context.SimpleEmbedAsync($"This server already has {Premium.PremiumConfig.LobbyLimit} lobbies created. " +
                            $"In order to create more you must become an ELO premium subscriber at {Premium.PremiumConfig.AltLink} join the server " +
                            $"{Premium.PremiumConfig.ServerInvite} to receive your role and then run the `claimpremium` command in your server.");
                        return;
                    }
                }

                lobby = new Lobby
                {
                    ChannelId = Context.Channel.Id,
                    GuildId = Context.Guild.Id,
                    PlayersPerTeam = playersPerTeam,
                    TeamPickMode = pickMode
                };
                db.Lobbies.Add(lobby);
                db.SaveChanges();
                await Context.SimpleEmbedAsync("New Lobby has been created\n" +
                    $"Players per team: {playersPerTeam}\n" +
                    $"Pick Mode: {pickMode}\n" +
                    $"NOTE: You can play multiple games per lobby. After a game has been created simply join the queue again and another game can be played.", Color.Green);
            }
        }

        [Command("SetPlayerCount", RunMode = RunMode.Sync)]
        [Alias("Set Player Count", "Set PlayerCount")]
        [Summary("Sets the amount of players per team.")]
        public virtual async Task SetPlayerAsync(int playersPerTeam)
        {
            using (var db = new Database())
            {
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Channel is not a lobby.", Color.Red);
                    return;
                }

                lobby.PlayersPerTeam = playersPerTeam;
                db.Lobbies.Update(lobby);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"There can now be up to {playersPerTeam} in each team.", Color.Green);
            }
        }

        [Command("SetPickMode", RunMode = RunMode.Sync)]
        [Alias("Set PickMode", "Set Pick Mode")]
        [Summary("Sets how players will be picked for teams in the current lobby.")]
        public virtual async Task SetPickModeAsync(PickMode pickMode)
        {
            using (var db = new Database())
            {
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Channel is not a lobby.", Color.Red);
                    return;
                }

                lobby.TeamPickMode = pickMode;
                db.Lobbies.Update(lobby);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Pick mode set.", Color.Green);
            }
        }

        /*[Command("ReactOnJoinLeave", RunMode = RunMode.Sync)]
        [Summary("Sets whether the bot will react or send a message when users join or leave a lobby.")]
        public virtual async Task ReactOnJoinLeaveAsync(bool react)
        {
            using (var db = new Database())
            {
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Channel is not a lobby.", Color.Red);
                    return;
                }

                lobby.ReactOnJoinLeave = react;
                db.Lobbies.Update(lobby);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"React on join/leave set.", Color.Green);
            }
        }*/

        [Command("PickModes", RunMode = RunMode.Async)]
        [Summary("Displays all pick modes to use with the SetPickMode command")]

        //[Alias("Pick Modes")] ignore this as it can potentially conflict with the lobby Pick command.
        public virtual async Task DisplayPickModesAsync()
        {
            await Context.SimpleEmbedAsync(string.Join("\n", Extensions.Extensions.EnumNames<PickMode>()), Color.Blue);
        }

        [Command("SetPickOrder", RunMode = RunMode.Sync)]
        [Alias("Set PickOrder", "Set Pick Order")]
        [Summary("Sets how captains pick players.")]
        public virtual async Task SetPickOrderAsync(CaptainPickOrder orderMode)
        {
            using (var db = new Database())
            {
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Channel is not a lobby.", Color.Red);
                    return;
                }

                lobby.CaptainPickOrder = orderMode;
                db.Lobbies.Update(lobby);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Captain pick order set.", Color.Green);
            }
        }

        [Command("PickOrders", RunMode = RunMode.Async)]
        [Summary("Shows pickorder settings for the SetPickOrder command")]
        public virtual async Task DisplayPickOrdersAsync()
        {
            var res = "`PickOne` - Captains each alternate picking one player until there are none remaining\n" +
                    "`PickTwo` - 1-2-2-1-1... Pick order. Captain 1 gets first pick, then Captain 2 picks 2 players,\n" +
                    "then Captain 1 picks 2 players and then alternate picking 1 player until teams are filled\n" +
                    "This is often used to reduce any advantage given for picking the first player.";
            await Context.SimpleEmbedAsync(res, Color.Blue);
        }

        [Command("SetReadyChannel", RunMode = RunMode.Sync)]
        [Alias("SetGameReadyAnnouncementChannel", "GameReadyAnnouncementsChannel", "GameReadyAnnouncements", "ReadyAnnouncements", "SetReadyAnnouncements")]
        [Summary("Set a channel to send game ready announcements for the current lobby to.")]
        public virtual async Task GameReadyAnnouncementChannel(SocketTextChannel destinationChannel = null)
        {
            if (destinationChannel == null)
            {
                await Context.SimpleEmbedAsync("You need to specify a channel for the announcements to be sent to.", Color.DarkBlue);
                return;
            }

            if (destinationChannel.Id == Context.Channel.Id)
            {
                await Context.SimpleEmbedAsync("You cannot send announcements to the current channel, instead run it in a lobby channel and mention the ready announcement channel.", Color.Red);
                return;
            }

            using (var db = new Database())
            {
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("This command must be run from within a lobby channel.", Color.Red);
                    return;
                }
                lobby.GameReadyAnnouncementChannel = destinationChannel.Id;
                db.Lobbies.Update(lobby);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Game ready announcements for the current lobby will be sent to {destinationChannel.Mention}", Color.Green);
            }
        }

        [Command("SetResultChannel", RunMode = RunMode.Sync)]
        [Alias("SetGameResultAnnouncementChannel", "SetGameResultAnnouncements", "GameResultAnnouncements")]
        [Summary("Set a channel to send game result announcements for the current lobby to.")]
        public virtual async Task GameResultAnnouncementChannel(SocketTextChannel destinationChannel = null)
        {
            if (destinationChannel == null)
            {
                await Context.SimpleEmbedAsync("You need to specify a channel for the announcements to be sent to.", Color.DarkBlue);
                return;
            }

            if (destinationChannel.Id == Context.Channel.Id)
            {
                await Context.SimpleEmbedAsync("You cannot send announcements to the current channel, instead run it in a lobby channel and mention the result announcement channel.", Color.Red);
                return;
            }

            using (var db = new Database())
            {
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("This command must be run from within a lobby channel.", Color.Red);
                    return;
                }
                lobby.GameResultAnnouncementChannel = destinationChannel.Id;
                db.Lobbies.Update(lobby);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Game results for the current lobby will be sent to {destinationChannel.Mention}", Color.Green);
            }
        }

        [Command("SetMinimumPoints", RunMode = RunMode.Sync)]
        [Summary("Set the minimum amount of points required to queue for the current lobby.")]
        public virtual async Task SetMinimumPointsAsync(int points)
        {
            using (var db = new Database())
            {
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Channel is not a lobby.", Color.Red);
                    return;
                }
                lobby.MinimumPoints = points;

                db.Lobbies.Update(lobby);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Minimum points required to join this lobby is now set to {points}.", Color.Green);
            }
        }

        [Command("ResetMinimumPoints", RunMode = RunMode.Sync)]
        [Summary("Resets minimum points to join the lobby, allowing all players to join.")]
        public virtual async Task ResetMinPointsAsync()
        {
            using (var db = new Database())
            {
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Channel is not a lobby.", Color.Red);
                    return;
                }
                lobby.MinimumPoints = null;

                db.Lobbies.Update(lobby);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Minimum points is now disabled for this lobby.", Color.Green);
            }
        }

        /*
        [Command("MapMode", RunMode = RunMode.Sync)]
        [Summary("Sets the map selection mode for the lobby.")]
        public virtual async Task MapModeAsync(MapMode mode)
        {
            if (mode == MapSelector.MapMode.Vote)
            {
                await Context.SimpleEmbedAsync("That mode is not available currently.", Color.DarkBlue);
                return;

                //Three options:
                //Last known map (if available)
                //Two random maps
                //Players vote on the maps and most voted is announced?
            }

            var lobby = Service.GetLobby(Context.Guild.Id, Context.Channel.Id);
            if (lobby == null)
            {
                await Context.SimpleEmbedAsync("Current channel is not a lobby.", Color.Red);
                return;
            }

            if (lobby.MapSelector == null)
            {
                lobby.MapSelector = new MapSelector();
            }

            lobby.MapSelector.Mode = mode;
            Service.SaveLobby(lobby);
            await Context.SimpleEmbedAsync("Mode set.", Color.Green);
        }

        [Command("MapMode", RunMode = RunMode.Async)]
        [Summary("Shows the current map selection mode for the lobby.")]
        public virtual async Task MapModeAsync()
        {
            var lobby = Service.GetLobby(Context.Guild.Id, Context.Channel.Id);
            if (lobby == null)
            {
                await Context.SimpleEmbedAsync("Current channel is not a lobby.", Color.Red);
                return;
            }

            if (lobby.MapSelector == null)
            {
                lobby.MapSelector = new MapSelector();
            }

            await Context.SimpleEmbedAsync($"Current Map Mode: {lobby.MapSelector?.Mode}", Color.Blue);
            return;
        }

        [Command("MapModes", RunMode = RunMode.Async)]
        [Summary("Shows all available map selection modes.")]
        public virtual async Task MapModes()
        {
            await Context.SimpleEmbedAsync(string.Join(", ", Extensions.EnumNames<MapSelector.MapMode>()), Color.Blue);
        }*/

        [Command("ClearMaps", RunMode = RunMode.Sync)]
        [Summary("Removes all maps set for the current lobby.")]
        public virtual async Task MapClear()
        {
            using (var db = new Database())
            {
                var lobby = db.GetLobby(Context.Channel);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Current channel is not a lobby.", Color.Red);
                    return;
                }

                var maps = db.Maps.AsQueryable().Where(x => x.ChannelId == Context.Channel.Id);
                db.RemoveRange(maps);
                db.SaveChanges();
                await Context.SimpleEmbedAsync("Maps cleared.", Color.Green);
            }
        }

        [Command("AddMaps", RunMode = RunMode.Sync)]
        [Alias("Add Maps", "Addmap", "Add map")]
        [Summary("Adds multiple maps to the map list.")]
        [Remarks("Separate the names using commas.")]
        public virtual async Task AddMapsAsync([Remainder]string commaSeparatedMapNames)
        {
            var mapNames = commaSeparatedMapNames.Split(',');
            if (!mapNames.Any()) return;

            using (var db = new Database())
            {
                var lobby = db.GetLobby(Context.Channel);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Current channel is not a lobby.", Color.Red);
                    return;
                }

                var currentMaps = db.Maps.AsQueryable().Where(x => x.ChannelId == Context.Channel.Id).ToArray();
                var mapViolations = new HashSet<string>(mapNames.Length);
                var addedMaps = new HashSet<string>(mapNames.Length);

                foreach (var map in mapNames)
                {
                    if (currentMaps.Any(x => x.MapName.Equals(map, System.StringComparison.OrdinalIgnoreCase)))
                    {
                        mapViolations.Add(map);
                        continue;
                    }

                    if (!addedMaps.Any(x => x.Equals(map, System.StringComparison.OrdinalIgnoreCase)))
                    {
                        db.Maps.Add(new Map
                        {
                            ChannelId = Context.Channel.Id,
                            MapName = map
                        });
                        addedMaps.Add(map);
                    }
                }
                db.SaveChanges();

                if (mapViolations.Count > 0)
                {
                    await Context.SimpleEmbedAsync($"Added Maps: {string.Join(", ", addedMaps)}\n" +
                                            $"Failed to Add: {string.Join(", ", mapViolations)}\n" +
                                            $"Duplicate maps found and ignored.");
                }
                else
                {
                    await Context.SimpleEmbedAsync("Map(s) added.", Color.Green);
                }
            }
        }

        [Command("DelMap", RunMode = RunMode.Sync)]
        [Summary("Removes the specified map from the map list.")]
        public virtual async Task RemoveMapAsync([Remainder]string mapName)
        {
            using (var db = new Database())
            {
                var lobby = db.GetLobby(Context.Channel);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Current channel is not a lobby.", Color.Red);
                    return;
                }

                var maps = db.Maps.AsQueryable().Where(x => x.ChannelId == Context.Channel.Id).ToArray();
                if (maps.Length == 0)
                {
                    await Context.SimpleEmbedAsync("There are no maps to remove.", Color.DarkBlue);
                    return;
                }

                var match = maps.SingleOrDefault(x => x.MapName.Equals(mapName, System.StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    db.Maps.Remove(match);
                    db.SaveChanges();
                    await Context.SimpleEmbedAsync("Map removed.", Color.Green);
                }
                else
                {
                    await Context.SimpleEmbedAsync("There was no map matching that name found.", Color.DarkBlue);
                }
            }
        }

        [Command("ToggleDms", RunMode = RunMode.Sync)]
        [Summary("Sets whether the bot will dm players when a game is ready.")]
        public virtual async Task ToggleDmsAsync()
        {
            using (var db = new Database())
            {
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Channel is not a lobby.", Color.Red);
                    return;
                }
                lobby.DmUsersOnGameReady = !lobby.DmUsersOnGameReady;
                db.Lobbies.Update(lobby);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"DM when games are ready: {lobby.DmUsersOnGameReady}", Color.Blue);
            }
        }

        [Command("SetDescription", RunMode = RunMode.Sync)]
        [Summary("Sets the lobbys description.")]
        public virtual async Task SetDescriptionAsync([Remainder]string description)
        {
            using (var db = new Database())
            {
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Channel is not a lobby.", Color.Red);
                    return;
                }
                lobby.Description = description;
                db.Lobbies.Update(lobby);
                db.SaveChanges();
                await Context.SimpleEmbedAsync("Lobby description set.", Color.Blue);
            }
        }

        [Command("DeleteLobby", RunMode = RunMode.Sync)]
        [Summary("Deletes the current lobby and all games played in it.")]
        public virtual async Task DeleteLobbyAsync()
        {
            await DeleteLobbyAsync(Context.Channel.Id);
        }

        [Command("DeleteLobby", RunMode = RunMode.Sync)]
        [Summary("Deletes the given lobby and all games played in it.")]
        public virtual async Task DeleteLobbyAsync(ulong lobbyId)
        {
            using (var db = new Database())
            {
                var lobby = db.Lobbies.Find(lobbyId);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Channel is not a lobby.", Color.Red);
                    return;
                }

                db.Lobbies.Remove(lobby);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Lobby and all games played in it have been removed.", Color.Green);
            }
        }

        [Command("DeleteLobby", RunMode = RunMode.Sync)]
        [Summary("Deletes the given lobby and all games played in it.")]
        public virtual async Task DeleteLobbyAsync(SocketTextChannel channel)
        {
            await DeleteLobbyAsync(channel.Id);
        }

        [Command("PurgeLobbies", RunMode = RunMode.Sync)]
        [Alias("PurgeLobbys")]
        [Summary("Deletes all lobbies that no longer have a channel associated with it.")]
        public virtual async Task PurgeLobbiesAsynnc()
        {
            using (var db = new Database())
            {
                //Find lobbies for the server where there is NO matching channel found
                var lobbies = db.Lobbies.AsQueryable().Where(x => x.GuildId == Context.Guild.Id).ToArray().AsQueryable().Where(x => !Context.Guild.Channels.Any(c => c.Id == x.ChannelId)).ToArray();
                if (lobbies.Length == 0)
                {
                    await Context.SimpleEmbedAsync("There are no lobbies to remove.", Color.Red);
                    return;
                }

                db.Lobbies.RemoveRange(lobbies);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"{lobbies.Length} Lobbies removed.", Color.Green);
            }
        }

        [Command("HideQueue", RunMode = RunMode.Sync)]
        [Summary("Sets whether players in queue are shown.")]
        [RequireBotPermission(GuildPermission.ManageMessages)]
        public virtual async Task AllowNegativeAsync(bool? hideQueue = null)
        {
            using (var db = new Database())
            {
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Channel is not a lobby.", Color.Red);
                    return;
                }
                if (hideQueue == null)
                {
                    await Context.SimpleEmbedAsync($"Current Hide Queue Setting: {lobby.HideQueue}", Color.Blue);
                    return;
                }
                lobby.HideQueue = hideQueue.Value;
                db.Lobbies.Update(lobby);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Hide Queue: {hideQueue.Value}", Color.Green);
            }
        }

        [Command("MentionUsersInReadyAnnouncement", RunMode = RunMode.Sync)]
        [Summary("Sets whether players are pinged in the ready announcement.")]
        [RequireBotPermission(GuildPermission.ManageMessages)]
        public virtual async Task MentionUsersReadyAsync(bool? mentionUsers = null)
        {
            using (var db = new Database())
            {
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Channel is not a lobby.", Color.Red);
                    return;
                }
                if (mentionUsers == null)
                {
                    await Context.SimpleEmbedAsync($"Current Mention Users Setting: {lobby.MentionUsersInReadyAnnouncement}", Color.Blue);
                    return;
                }
                lobby.MentionUsersInReadyAnnouncement = mentionUsers.Value;
                db.Lobbies.Update(lobby);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Mention Users: {mentionUsers.Value}", Color.Green);
            }
        }

        [Command("SetMultiplier", RunMode = RunMode.Sync)]
        [Summary("Sets the lobby score multiplier.")]
        public virtual async Task SetLobbyMultiplier(double multiplier)
        {
            using (var db = new Database())
            {
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Channel is not a lobby.", Color.Red);
                    return;
                }

                lobby.LobbyMultiplier = multiplier;
                db.Lobbies.Update(lobby);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Multiplier set.", Color.Green);
            }
        }

        [Command("LobbyMultiplierLoss", RunMode = RunMode.Sync)]
        [Summary("Sets whether the lobby multiplier affects the amount of points removed from users.")]
        public virtual async Task SetLossToggleMultiplier(bool? value = null)
        {
            using (var db = new Database())
            {
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Channel is not a lobby.", Color.Red);
                    return;
                }

                if (value == null)
                {
                    await Context.SimpleEmbedAsync($"Current Multiply Loss Value Setting: {lobby.MultiplyLossValue}");
                    return;
                }

                lobby.MultiplyLossValue = value.Value;
                db.Lobbies.Update(lobby);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Multiplier will affect the loss amount: {lobby.MultiplyLossValue}", Color.Green);
            }
        }

        [Command("SetHighLimitMultiplier", RunMode = RunMode.Sync)]
        [Summary("Sets a multiplier for users who have a higher amount of points than what is defined in the SetHighLimit command.")]
        public virtual async Task SetHighLimitMultiplier(double multiplier = 0.5)
        {
            using (var db = new Database())
            {
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Channel is not a lobby.", Color.Red);
                    return;
                }

                lobby.ReductionPercent = multiplier;
                db.Lobbies.Update(lobby);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"High Limit multiplier set, when users exceed `{(lobby.HighLimit.HasValue ? lobby.HighLimit.Value.ToString() : "N/A (Configure using the SetHighLimit command)")}` points, " +
                    $"their points received will be multiplied by `{multiplier}`, it is recommended to set this to a value such as `0.5` in for lower ranked lobbies " +
                    $"so higher ranked players are not rewarded as much for winning in lower ranked lobbies.", Color.Green);
            }
        }

        [Command("SetHighLimit", RunMode = RunMode.Sync)]
        [Summary("Sets max user points before point reduction multiplier is applied.")]
        public virtual async Task SetHighLimit(int? highLimit = null)
        {
            using (var db = new Database())
            {
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Channel is not a lobby.", Color.Red);
                    return;
                }

                lobby.HighLimit = highLimit;
                db.Lobbies.Update(lobby);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Max user points before point reduction set.", Color.Green);
            }
        }

        [Command("HostModes")]
        [Summary("Displays a list of host selection modes available")]
        public virtual async Task ShowHostSelectionModes()
        {
            await Context.SimpleEmbedAsync(string.Join("\n", Extensions.Extensions.EnumNames<HostSelection>()));
        }

        [Command("SetHostMode", RunMode = RunMode.Sync)]
        [Summary("Sets if and how the host is selected.")]
        public virtual async Task SetHostModeAsync(HostSelection hostMode)
        {
            using (var db = new Database())
            {
                var lobby = db.Lobbies.FirstOrDefault(x => x.ChannelId == Context.Channel.Id);
                if (lobby == null)
                {
                    await Context.SimpleEmbedAsync("Channel is not a lobby.", Color.Red);
                    return;
                }

                lobby.HostSelectionMode = hostMode;
                db.Lobbies.Update(lobby);
                db.SaveChanges();
                await Context.SimpleEmbedAsync($"Host Selection Mode set to: {lobby.HostSelectionMode}", Color.Green);
            }
        }
    }
}