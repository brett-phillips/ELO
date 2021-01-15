﻿using Discord;
using Discord.Commands;
using Discord.WebSocket;
using ELO.Entities;
using ELO.Models;
using ELO.Preconditions;
using ELO.Services;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ELO.Extensions;
using ELO.Services.Reactive;

namespace ELO.Modules
{
    [RequireContext(ContextType.Guild)]
    public partial class Info : ModuleBase<ShardedCommandContext>
    {
        private readonly ReactiveService _reactive;
        public HttpClient HttpClient { get; }

        public CommandService CommandService { get; }

        public HelpService HelpService { get; }

        public GameService GameService { get; }

        public PermissionService PermissionService { get; }

        public PremiumService Premium { get; }

        public Info(HttpClient httpClient, CommandService commandService, HelpService helpService, ReactiveService reactive, GameService gameService, PermissionService permissionService, PremiumService premium)
        {
            _reactive = reactive;
            HttpClient = httpClient;
            CommandService = commandService;
            HelpService = helpService;
            GameService = gameService;
            PermissionService = permissionService;
            Premium = premium;
        }

        [Command("Invite")]
        [Summary("Returns the bot invite")]
        public virtual async Task InviteAsync()
        {
            await ReplyAsync(null, false, $"Invite: https://discordapp.com/oauth2/authorize?client_id={Context.Client.CurrentUser.Id}&scope=bot&permissions=8".QuickEmbed());
        }

        [Command("Help")]
        [Summary("Shows available commands based on the current user permissions")]
        public virtual async Task HelpAsync()
        {
            using (var db = new Database())
            {
                if (!PermissionService.PermissionCache.ContainsKey(Context.Guild.Id))
                {
                    var comp = db.GetOrCreateCompetition(Context.Guild.Id);
                    var guildModel = new PermissionService.CachedPermissions
                    {
                        GuildId = Context.Guild.Id,
                        AdminId = comp.AdminRole,
                        ModId = comp.ModeratorRole
                    };

                    var permissions = db.Permissions.AsQueryable().Where(x => x.GuildId == Context.Guild.Id).ToArray();
                    foreach (var commandGroup in CommandService.Commands.GroupBy(x => x.Name.ToLower()))
                    {
                        var match = permissions.FirstOrDefault(x => x.CommandName.Equals(commandGroup.Key, StringComparison.OrdinalIgnoreCase));
                        if (match == null)
                        {
                            guildModel.Cache.Add(commandGroup.Key.ToLower(), null);
                        }
                        else
                        {
                            guildModel.Cache.Add(commandGroup.Key.ToLower(), new PermissionService.CachedPermissions.CachedPermission
                            {
                                CommandName = commandGroup.Key.ToLower(),
                                Level = match.Level
                            });
                        }
                    }

                    PermissionService.PermissionCache[Context.Guild.Id] = guildModel;
                }
            }
            await GenerateHelpAsync();
        }

        [Command("FullHelp")]
        [RequirePermission(PermissionLevel.Moderator)]
        [Summary("Displays all commands without checking permissions")]
        public virtual async Task FullHelpAsync()
        {
            await GenerateHelpAsync(false);
        }

        public virtual async Task GenerateHelpAsync(bool checkPreconditions = true)
        {
            try
            {
                var res = await HelpService.PagedHelpAsync(Context, checkPreconditions, null,
                "You can react with the :1234: emote and type a page number to go directly to that page too,\n" +
                "otherwise react with the arrows (◀ ▶) to change pages.\n");
                if (res != null)
                {
                    await _reactive.SendPagedMessageAsync(Context, Context.Channel, res.ToCallBack().WithDefaultPagerCallbacks().WithJump());
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        [Command("Shards")]
        [Summary("Displays information about all shards")]
        public virtual async Task ShardInfoAsync()
        {
            var info = Context.Client.Shards.Select(x => $"[{x.ShardId}] {x.Status} {x.ConnectionState} - Guilds: {x.Guilds.Count} Users: {x.Guilds.Sum(g => g.MemberCount)}");
            await ReplyAsync($"```\n" + $"{string.Join("\n", info)}\n" + $"```");
        }

        [RateLimit(1, 1, Measure.Minutes, RateLimitFlags.ApplyPerGuild)]
        [Command("Stats")]
        [Summary("Bot Info and Stats")]
        public virtual async Task InformationAsync()
        {
            string changes;
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/PassiveModding/ELO/commits");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; MSIE 10.0; Windows NT 6.2; WOW64; Trident/6.0)");
            var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                changes = "There was an error fetching the latest changes.";
            }
            else
            {
                dynamic result = JArray.Parse(await response.Content.ReadAsStringAsync());
                changes = $"[{((string)result[0].sha).Substring(0, 7)}]({result[0].html_url}) {result[0].commit.message}\n" + $"[{((string)result[1].sha).Substring(0, 7)}]({result[1].html_url}) {result[1].commit.message}\n" + $"[{((string)result[2].sha).Substring(0, 7)}]({result[2].html_url}) {result[2].commit.message}";
            }

            var embed = new EmbedBuilder();

            embed.WithAuthor(
                x =>
                {
                    x.IconUrl = Context.Client.CurrentUser.GetAvatarUrl();
                    x.Name = $"{Context.Client.CurrentUser.Username}'s Official Invite";
                    x.Url = $"https://discordapp.com/oauth2/authorize?client_id={Context.Client.CurrentUser.Id}&scope=bot&permissions=2146958591";
                });
            embed.AddField("Changes", changes.FixLength());

            embed.AddField("Members", $"Bot: {Context.Client.Guilds.Sum(x => x.Users.Count(z => z.IsBot))}\nHuman: {Context.Client.Guilds.Sum(x => x.Users.Count(z => !z.IsBot))}\nPresent: {Context.Client.Guilds.Sum(x => x.Users.Count(u => u.Status != UserStatus.Offline))}", true);
            embed.AddField("Members", $"Online: {Context.Client.Guilds.Sum(x => x.Users.Count(z => z.Status == UserStatus.Online))}\nAFK: {Context.Client.Guilds.Sum(x => x.Users.Count(z => z.Status == UserStatus.Idle))}\nDND: {Context.Client.Guilds.Sum(x => x.Users.Count(u => u.Status == UserStatus.DoNotDisturb))}", true);
            embed.AddField("Channels", $"Text: {Context.Client.Guilds.Sum(x => x.TextChannels.Count)}\nVoice: {Context.Client.Guilds.Sum(x => x.VoiceChannels.Count)}\nTotal: {Context.Client.Guilds.Sum(x => x.Channels.Count)}", true);
            embed.AddField("Guilds", $"Count: {Context.Client.Guilds.Count}\nTotal Users: {Context.Client.Guilds.Sum(x => x.MemberCount)}\nTotal Cached: {Context.Client.Guilds.Sum(x => x.Users.Count())}\n", true);
            var orderedShards = Context.Client.Shards.OrderByDescending(x => x.Guilds.Count).ToList();
            embed.AddField("Shards", $"Shards: {Context.Client.Shards.Count}\nMax: G:{orderedShards.First().Guilds.Count} ID:{orderedShards.First().ShardId}\nMin: G:{orderedShards.Last().Guilds.Count} ID:{orderedShards.Last().ShardId}", true);
            embed.AddField("Commands", $"Commands: {CommandService.Commands.Count()}\nAliases: {CommandService.Commands.Sum(x => x.Attributes.Count)}\nModules: {CommandService.Modules.Count()}", true);
            embed.AddField(":hammer_pick:", $"Heap: {Math.Round(GC.GetTotalMemory(true) / (1024.0 * 1024.0), 2)} MB\nUp: {(DateTime.Now - Process.GetCurrentProcess().StartTime).ToString(@"dd\D\ hh\H\ mm\M\ ss\S")}", true);
            embed.AddField(":beginner:", $"Written by: [PassiveModding](https://github.com/PassiveModding)\nDiscord.Net {DiscordConfig.Version}", true);

            await ReplyAsync("", false, embed.Build());
        }

        [Command("Ranks", RunMode = RunMode.Async)]
        [Summary("Displays information about the server's current ranks")]
        [RequirePermission(PermissionLevel.Registered)]
        public virtual async Task ShowRanksAsync()
        {
            using (var db = new Database())
            {
                var comp = db.GetOrCreateCompetition(Context.Guild.Id);
                var ranks = db.Ranks.AsQueryable().Where(x => x.GuildId == Context.Guild.Id).ToList();
                if (ranks.Count == 0)
                {
                    await Context.SimpleEmbedAsync("There are currently no ranks set up.", Color.Blue);
                    return;
                }

                var msg = ranks.OrderByDescending(x => x.Points).Select(x => $"{MentionUtils.MentionRole(x.RoleId)} - ({x.Points}) W: (+{x.WinModifier ?? comp.DefaultWinModifier}) L: (-{x.LossModifier ?? comp.DefaultLossModifier})").ToArray();
                await Context.SimpleEmbedAsync(string.Join("\n", msg), Color.Blue);
            }
        }

        [Command("Profile", RunMode = RunMode.Async)] // Please make default command name "Stats"
        [Alias("Info", "GetUser")]
        [Summary("Displays information about you or the specified user.")]
        [RequirePermission(PermissionLevel.Registered)]
        public virtual async Task InfoAsync(SocketGuildUser user = null)
        {
            if (user == null)
            {
                user = Context.User as SocketGuildUser;
            }

            using (var db = new Database())
            {
                var player = db.Players.Find(Context.Guild.Id, user.Id);
                if (player == null)
                {
                    if (user.Id == Context.User.Id)
                    {
                        await Context.SimpleEmbedAsync("You are not registered.", Color.DarkBlue);
                    }
                    else
                    {
                        await Context.SimpleEmbedAsync("That user is not registered.", Color.Red);
                    }
                    return;
                }

                var ranks = db.Ranks.AsQueryable().Where(x => x.GuildId == Context.Guild.Id).ToList();
                var maxRank = ranks.Where(x => x.Points < player.Points).OrderByDescending(x => x.Points).FirstOrDefault();
                string rankStr = null;
                if (maxRank != null)
                {
                    rankStr = $"Rank: {MentionUtils.MentionRole(maxRank.RoleId)} ({maxRank.Points})\n";
                }

                var info = $"{player.GetDisplayNameSafe()} Stats\n" + // Use Title?
                            $"Points: {player.Points}\n" +
                            rankStr +
                            $"Wins: {player.Wins}\n" +
                            $"Losses: {player.Losses}\n" +
                            $"Draws: {player.Draws}\n" +
                            $"Games: {player.Games}\n";

                if (player.Kills > 0 || player.Deaths > 0)
                {
                    int deathCount = player.Deaths == 0 ? 1 : player.Deaths;
                    double kdr = Math.Round((double)player.Kills / deathCount, 2);
                    info += $"Kills: {player.Kills}\n" +
                        $"Deaths: {player.Deaths}\n" +
                        $"KDR: {kdr}\n";
                }

                info += $"Registered At: {player.RegistrationDate.ToString("dd MMM yyyy")} {player.RegistrationDate.ToShortTimeString()}";

                await Context.SimpleEmbedAsync(info, Color.Blue);
            }

            //TODO: Add game history (last 5) to this response
            //+ if they were on the winning team?
            //maybe only games with a decided result should be shown?
        }

        [Command("Leaderboard", RunMode = RunMode.Async)]
        [Alias("lb", "top20")]
        [Summary("Shows the current server-wide leaderboard.")]
        [RequirePermission(PermissionLevel.Registered)]
        [RateLimit(1, 10, Measure.Seconds, RateLimitFlags.ApplyPerGuild)]
        [Priority(1)]
        public virtual Task LeaderboardAsync(int page = 1)
        {
            return LeaderboardAsync(LeaderboardSortMode.points, page);
        }

        [Command("Leaderboard", RunMode = RunMode.Async)]
        [Alias("lb", "top20")]
        [Summary("Shows the current server-wide leaderboard.")]
        [RequirePermission(PermissionLevel.Registered)]
        [RateLimit(1, 10, Measure.Seconds, RateLimitFlags.ApplyPerGuild)]
        public virtual async Task LeaderboardAsync(LeaderboardSortMode mode = LeaderboardSortMode.points, int page = 1)
        {
            if (page <= 0)
            {
                page = 1;
            }
            else if (page > 1)
            {
                if (!Premium.IsPremium(Context.Guild.Id))
                {
                    await Context.SimpleEmbedAsync($"In order to access a complete leaderboard, consider joining ELO premium at {Premium.PremiumConfig.AltLink}, " +
                        $"patrons must also be members of the ELO server at: {Premium.PremiumConfig.ServerInvite}\n" +
                        $"Free servers are limited to just the first page.");
                    page = 1;
                }
            }

            using (var db = new Database())
            {
                //Retrieve players in the current guild from database
                var users = db.Players.AsNoTracking().AsQueryable().Where(x => x.GuildId == Context.Guild.Id);
                int count = users.Count();
                int pageSize = 20;
                int skipCount = (page - 1) * pageSize;

                //Order players by score and then split them into groups of 20 for pagination
                Player[] players;
                switch (mode)
                {
                    case LeaderboardSortMode.point:
                        players = users.OrderByDescending(x => x.Points).Skip(skipCount).Take(pageSize).ToArray();
                        break;

                    case LeaderboardSortMode.wins:
                        players = users.OrderByDescending(x => x.Wins).Skip(skipCount).Take(pageSize).ToArray();
                        break;

                    case LeaderboardSortMode.losses:
                        players = users.OrderByDescending(x => x.Losses).Skip(skipCount).Take(pageSize).ToArray();
                        break;

                    case LeaderboardSortMode.wlr:
                        players = users.OrderByDescending(x => x.Losses == 0 ? x.Wins : (double)x.Wins / x.Losses).Skip(skipCount).Take(pageSize).ToArray();
                        break;

                    case LeaderboardSortMode.games:
                        players = users.OrderByDescending(x => x.Draws + x.Wins + x.Losses).Skip(skipCount).Take(pageSize).ToArray();
                        break;

                    case LeaderboardSortMode.kills:
                        players = users.OrderByDescending(x => x.Kills).Skip(skipCount).Take(pageSize).ToArray();
                        break;

                    case LeaderboardSortMode.kdr:
                        players = users.OrderByDescending(x => (double)x.Kills / (x.Deaths == 0 ? 1 : x.Deaths)).Skip(skipCount).Take(pageSize).ToArray();
                        break;

                    default:
                        return;
                }
                if (players.Length == 0)
                {
                    await Context.SimpleEmbedAsync("There are no players to display for this page of the leaderboard.", Color.Blue);
                    return;
                }

                var embed = new EmbedBuilder();
                embed.Title = $"{Context.Guild.Name} - Leaderboard [{page}]";
                embed.Color = Color.Blue;
                embed.Description = GetPlayerLines(players, skipCount + 1, mode);
                embed.WithFooter($"{count} users");

                await ReplyAsync(null, false, embed.Build());
            }
        }

        //Returns the updated index and the formatted player lines
        public string GetPlayerLines(Player[] players, int startValue, LeaderboardSortMode mode)
        {
            var sb = new StringBuilder();

            //Iterate through the players and add their summary line to the list.
            foreach (var player in players)
            {
                switch (mode)
                {
                    case LeaderboardSortMode.point:
                        sb.AppendLine($"{startValue}: {player.GetDisplayNameSafe()} - `{player.Points}`");
                        break;

                    case LeaderboardSortMode.wins:
                        sb.AppendLine($"{startValue}: {player.GetDisplayNameSafe()} - `{player.Wins}`");
                        break;

                    case LeaderboardSortMode.losses:
                        sb.AppendLine($"{startValue}: {player.GetDisplayNameSafe()} - `{player.Losses}`");
                        break;

                    case LeaderboardSortMode.wlr:
                        var wlr = player.Losses == 0 ? player.Wins : (double)player.Wins / player.Losses;
                        sb.AppendLine($"{startValue}: {player.GetDisplayNameSafe()} - `{Math.Round(wlr, 2, MidpointRounding.AwayFromZero)}`");
                        break;

                    case LeaderboardSortMode.games:
                        sb.AppendLine($"{startValue}: {player.GetDisplayNameSafe()} - `{player.Games}`");
                        break;

                    case LeaderboardSortMode.kills:
                        sb.AppendLine($"{startValue}: {player.GetDisplayNameSafe()} - `{player.Kills}`");
                        break;

                    case LeaderboardSortMode.kdr:
                        sb.AppendLine($"{startValue}: {player.GetDisplayNameSafe()} - `{(double)player.Kills / (player.Deaths == 0 ? 1 : player.Deaths)}`");
                        break;
                }

                startValue++;
            }

            //Return the updated start value and the list of player lines.
            return sb.ToString();
        }

        private CommandInfo Command { get; set; }

        protected override void BeforeExecute(CommandInfo command)
        {
            Command = command;
            base.BeforeExecute(command);
        }
    }
}